using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using ClickStat.Infrastructure.Data.Context;
using ClickStat.Infrastructure.Data.Model;
using ClickStat.Infrastructure.Diagnostics;
using ClickStat.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Timer = System.Timers.Timer;

namespace ClickStat.Infrastructure.Data;

/// <summary>
/// Reconstructs words from individual key events, tracks frequencies,
/// bigrams, and 2-word phrases. Flushes to DB every 30 s.
/// </summary>
public sealed class WordProcessor : IDisposable
{
    private const int FlushIntervalSeconds = 30;
    private const int MinWordLength        = 2;
    private const int MaxWordLength        = 32;
    private const int VisibleMinimumCount  = 6;

    private readonly string _dbPath;
    private readonly string _captureSettingPath;
    private readonly Timer  _timer;
    private readonly object _lock = new();
    private readonly SemaphoreSlim _flushGate = new(1, 1);

    // In-memory buffers
    private readonly StringBuilder              _buffer   = new();
    private readonly Queue<string>              _lastWords = new(3); // sliding window for phrases
    private string?                             _lastKey;            // for bigrams
    private readonly Dictionary<string, int>   _words    = new();
    private readonly Dictionary<string, int>   _bigrams  = new();
    private readonly Dictionary<string, int>   _phrases  = new();
    private readonly object _schemaGate = new();
    private bool _schemaReady;
    private volatile bool _captureEnabled;

    /// <summary>Controls collection of words, phrases and key bigrams.</summary>
    public bool CaptureEnabled
    {
        get => _captureEnabled;
        set
        {
            lock (_lock)
            {
                if (_captureEnabled == value) return;
                _captureEnabled = value;
                _buffer.Clear();
                _lastWords.Clear();
                _lastKey = null;
                if (!value)
                {
                    _words.Clear();
                    _bigrams.Clear();
                    _phrases.Clear();
                    _timer.Stop();
                }
                else
                    _timer.Start();
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_captureSettingPath)!);
                File.WriteAllText(_captureSettingPath, value ? "1" : "0");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public WordProcessor()
    {
        var docs   = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        _dbPath    = Path.Combine(docs, "KeyClick", "key_statistics.db");
        _captureSettingPath = Path.Combine(docs, "KeyClick", "word-capture.txt");
        try { _captureEnabled = !File.Exists(_captureSettingPath) || File.ReadAllText(_captureSettingPath).Trim() != "0"; }
        catch (IOException) { _captureEnabled = true; }
        catch (UnauthorizedAccessException) { _captureEnabled = true; }

        _timer = new Timer(FlushIntervalSeconds * 1000) { AutoReset = true };
        _timer.Elapsed += async (_, _) => await Flush();
        if (_captureEnabled) _timer.Start();
    }

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Call from MainViewModel.OnKeyDownReceived when no Ctrl/Alt held.
    /// Uses ToUnicode to get the actual character for ANY keyboard layout (RU, EN, DE, …).
    /// </summary>
    public void ProcessKey(Keys key, bool shift = false)
    {
        if (!_captureEnabled) return;
        lock (_lock)
        {
            if (!_captureEnabled) return;
            if (key == Keys.Back)
            {
                if (_buffer.Length > 0) _buffer.Remove(_buffer.Length - 1, 1);
                _lastKey = null;
                return;
            }

            // Try to resolve the actual unicode character for current layout + modifier state
            char? ch = ResolveChar(key, shift);

            if (ch.HasValue && char.IsLetter(ch.Value))
            {
                if (_buffer.Length <= MaxWordLength)
                    _buffer.Append(char.ToLowerInvariant(ch.Value));
                else
                {
                    _lastKey = null;
                    return;
                }

                // Key-level bigram (layout-independent: raw key names)
                if (_lastKey != null)
                {
                    string bigram = $"{_lastKey}|{key}";
                    _bigrams[bigram] = _bigrams.GetValueOrDefault(bigram) + 1;
                }
                _lastKey = key.ToString();
                return;
            }

            // Word boundary: space, punctuation, enter, tab, etc.
            if (IsWordBoundary(key) || (ch.HasValue && (char.IsWhiteSpace(ch.Value) || char.IsPunctuation(ch.Value))))
                CompleteWord();

            if (key is Keys.Enter or Keys.Return or Keys.Escape or Keys.Delete or Keys.Next or Keys.Prior ||
                ch.HasValue && char.IsPunctuation(ch.Value))
                _lastWords.Clear();

            _lastKey = null;
        }
    }

    /// <summary>
    /// Converts a virtual key to the character produced in the current keyboard layout.
    /// Uses flag 4 (peek-only) to avoid consuming dead key state.
    /// </summary>
    private static char? ResolveChar(Keys key, bool shift)
    {
        var keyState = new byte[256];
        var keyCode = key & Keys.KeyCode;
        int virtualKey = (int)keyCode;
        if (virtualKey is <= 0 or > 255) return null;

        keyState[virtualKey] = 0x80;
        if ((GetKeyState((int)Keys.Capital) & 0x0001) != 0)
            keyState[(int)Keys.Capital] = 0x01;

        if (shift)
        {
            keyState[(int)Keys.ShiftKey] = 0x80;
            keyState[(int)Keys.LShiftKey] = 0x80;
        }

        var sb = new StringBuilder(4);
        var layout = LayoutService.GetCurrentKeyboardLayoutHandle();
        uint scanCode = (uint)MapVirtualKeyEx((uint)virtualKey, 0, layout);
        int result = ToUnicodeEx((uint)virtualKey, scanCode, keyState, sb, 4, 4, layout);
        if (result > 0 && sb.Length > 0)
            return sb[0];
        return null;
    }

    [DllImport("user32.dll")]
    private static extern int ToUnicodeEx(
        uint wVirtKey,
        uint wScanCode,
        byte[] lpKeyState,
        [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pwszBuff,
        int cchBuff,
        uint wFlags,
        IntPtr dwhkl);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKeyEx(uint uCode, uint uMapType, IntPtr dwhkl);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    public void ClearBuffer()
    {
        if (!_captureEnabled) return;
        lock (_lock)
        {
            _buffer.Clear();
            _lastWords.Clear();
            _lastKey = null;
        }
    }

    // ── DB data access ────────────────────────────────────────────────────

    public async Task<List<WordStatistics>> GetTopWords(int limit = 50)
    {
        await using var ctx = new DataContext(_dbPath);
        return await ctx.WordStatistics.AsNoTracking()
            .Where(w => w.Count >= VisibleMinimumCount)
            .OrderByDescending(w => w.Count)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<List<KeyBigram>> GetTopBigrams(int limit = 30)
    {
        EnsureSchema();
        await using var ctx = new DataContext(_dbPath);
        return await ctx.KeyBigrams
            .Where(b => b.Count >= VisibleMinimumCount)
            .OrderByDescending(b => b.Count)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<List<WordPhrase>> GetTopPhrases(int limit = 30)
    {
        await using var ctx = new DataContext(_dbPath);
        return await ctx.WordPhrases.AsNoTracking()
            .Where(p => p.Count >= VisibleMinimumCount)
            .OrderByDescending(p => p.Count)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<int> GetTotalWordsTyped()
    {
        await using var ctx = new DataContext(_dbPath);
        return await ctx.WordStatistics.Where(w => w.Count >= VisibleMinimumCount).SumAsync(w => w.Count);
    }

    public async Task<int> GetUniqueWordsCount()
    {
        await using var ctx = new DataContext(_dbPath);
        return await ctx.WordStatistics.CountAsync(w => w.Count >= VisibleMinimumCount);
    }

    // ── Internal ──────────────────────────────────────────────────────────

    private static readonly HashSet<char> RussianVowels = new()
    {
        'а','е','ё','и','й','о','у','ы','э','ю','я'
    };

    private static readonly HashSet<string> RussianShortWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "а","я","в","к","с","у","о",
        "да","не","на","но","он","мы","вы","ты","то","та","те","же","ли","из","за","по","до","от",
        "во","со","об","ну","их","ее","её","уж","ой","ах",
        "это","так","там","тут","тот","кто","что","как","где","все","всё","еще","ещё","уже","или",
        "для","без","под","над","при","про","чем","она","оно","они","его","мой","моя","дом","код",
        "мир","сын","два","три","раз"
    };

    public static bool IsAcceptableWord(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return false;

        string word = raw.Trim().ToLowerInvariant();
        if (word.Length < MinWordLength || word.Length > MaxWordLength) return false;
        if (word.Any(c => !char.IsLetter(c))) return false;
        if (HasExcessiveRepeats(word)) return false;

        bool hasLatin = word.Any(IsLatinLetter);
        bool hasRussian = word.Any(IsRussianLetter);
        if (hasLatin == hasRussian) return false;

        return hasLatin
            ? IsKnownEnglishWord(word)
            : IsLikelyRussianWord(word);
    }

    private static bool IsCheapWordCandidate(string word)
    {
        if (word.Length is < MinWordLength or > MaxWordLength) return false;
        bool latin = false, russian = false;
        char previous = '\0';
        int run = 0;
        foreach (char c in word)
        {
            if (IsLatinLetter(c)) latin = true;
            else if (IsRussianLetter(c)) russian = true;
            else return false;
            run = c == previous ? run + 1 : 1;
            if (run > 2) return false;
            previous = c;
        }
        return latin != russian;
    }

    public static bool IsRecognizedWord(string word) => IsAcceptableWord(word);

    public static bool IsEnglishWord(string word) => GetWordLanguage(word) == "EN";

    public static bool IsRussianWord(string word) => GetWordLanguage(word) == "RU";

    public static string? GetWordLanguage(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        string word = raw.Trim().ToLowerInvariant();
        bool hasLatin = word.Any(IsLatinLetter);
        bool hasRussian = word.Any(IsRussianLetter);
        if (hasLatin == hasRussian) return null;
        return hasLatin ? "EN" : "RU";
    }

    public static bool IsPhraseLanguage(string phrase, string language)
    {
        var parts = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length is 2 or 3 &&
               parts.All(IsAcceptableWord) &&
               parts.All(part => GetWordLanguage(part) == language);
    }

    private static bool IsAcceptablePhrase(string phrase)
    {
        var parts = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is not (2 or 3) || !parts.All(IsAcceptableWord)) return false;

        string? language = GetWordLanguage(parts[0]);
        return language != null && parts.All(part => GetWordLanguage(part) == language);
    }

    private static readonly HashSet<char> EnglishVowels = new() { 'a','e','i','o','u' };

    private static bool IsKnownEnglishWord(string word)
    {
        // Allow any word that looks like real English (has vowels, no impossible runs).
        // The dictionary is used only to BOOST recognition, not to gate all words.
        // Gaming gibberish (wasd, hjkl) is caught by the vowel/run heuristics.
        if (!word.Any(c => EnglishVowels.Contains(c))) return false;

        // Words in the dictionary are definitely valid
        if (EnglishDictionary.Contains(word)) return true;

        // Unknown words: accept if they have plausible vowel/consonant structure
        // (no more than 4 consecutive consonants or 3 consecutive vowels)
        return !HasImpossibleRuns(word, c => EnglishVowels.Contains(c), maxConsonants: 4, maxVowels: 3);
    }

    private static bool IsLikelyRussianWord(string word)
    {
        if (!word.Any(RussianVowels.Contains)) return false;

        if (word.Length <= 3)
        {
            if (RussianShortWords.Contains(word)) return true;
            return word.Length == 3 &&
                   IsRussianConsonant(word[0]) &&
                   RussianVowels.Contains(word[1]) &&
                   IsRussianConsonant(word[2]);
        }

        return !HasImpossibleRuns(word, RussianVowels.Contains, maxConsonants: 4, maxVowels: 3);
    }

    private static bool HasExcessiveRepeats(string word)
    {
        var run = 1;
        for (int i = 1; i < word.Length; i++)
        {
            run = word[i] == word[i - 1] ? run + 1 : 1;
            if (run > 2) return true;
        }
        return false;
    }

    private static bool HasImpossibleRuns(string word, Func<char, bool> isVowel, int maxConsonants, int maxVowels)
    {
        var consonants = 0;
        var vowels = 0;

        foreach (char c in word)
        {
            if (isVowel(c))
            {
                vowels++;
                consonants = 0;
                if (vowels > maxVowels) return true;
            }
            else
            {
                consonants++;
                vowels = 0;
                if (consonants > maxConsonants) return true;
            }
        }

        return false;
    }

    private static bool IsLatinLetter(char c) => c is >= 'a' and <= 'z';
    private static bool IsRussianLetter(char c) => c is (>= 'а' and <= 'я') or 'ё';
    private static bool IsRussianConsonant(char c) => IsRussianLetter(c) && !RussianVowels.Contains(c);

    private void CompleteWord()
    {
        if (_buffer.Length < MinWordLength)
        {
            if (_buffer.Length > 0) _lastWords.Clear();
            _buffer.Clear();
            return;
        }

        string word = _buffer.ToString();
        _buffer.Clear();

        if (!IsCheapWordCandidate(word))
        {
            _lastWords.Clear();
            return;
        }

        string? language = GetWordLanguage(word);
        if (_lastWords.Count > 0 && GetWordLanguage(_lastWords.Last()) != language)
            _lastWords.Clear();

        _words[word] = _words.GetValueOrDefault(word) + 1;
        PerformanceLog.CountWord();

        // Phrase tracking (2-word window)
        if (_lastWords.Count > 0)
        {
            string phrase2 = $"{_lastWords.Last()} {word}";
            _phrases[phrase2] = _phrases.GetValueOrDefault(phrase2) + 1;
        }
        if (_lastWords.Count > 1)
        {
            var arr = _lastWords.ToArray();
            string phrase3 = $"{arr[^2]} {arr[^1]} {word}";
            _phrases[phrase3] = _phrases.GetValueOrDefault(phrase3) + 1;
        }

        // Maintain sliding window
        _lastWords.Enqueue(word);
        while (_lastWords.Count > 3) _lastWords.Dequeue();
    }

    public Task FlushAsync() => Flush();

    private async Task Flush()
    {
        using var timing = PerformanceLog.Measure("words.flush");
        await _flushGate.WaitAsync();
        try
        {
            await FlushCore();
        }
        finally
        {
            _flushGate.Release();
        }
    }

    private async Task FlushCore()
    {
        Dictionary<string, int> wordSnap, bigramSnap, phraseSnap;
        lock (_lock)
        {
            if (!_captureEnabled) return;
            if (_words.Count == 0 && _bigrams.Count == 0 && _phrases.Count == 0) return;
            wordSnap   = new Dictionary<string, int>(_words);
            bigramSnap = new Dictionary<string, int>(_bigrams);
            phraseSnap = new Dictionary<string, int>(_phrases);
            _words.Clear(); _bigrams.Clear(); _phrases.Clear();
        }

        await using var ctx = new DataContext(_dbPath);
        EnsureSchema();
        var tx = await ctx.Database.BeginTransactionAsync();
        try
        {
            foreach (var (word, count) in wordSnap)
            {
                await ctx.Database.ExecuteSqlInterpolatedAsync($@"
                    INSERT INTO WordStatistics (Word, Count, LastTyped, IsKnown)
                    VALUES ({word}, {count}, {DateTime.Now}, {false})
                    ON CONFLICT(Word) DO UPDATE SET
                        Count = Count + excluded.Count,
                        LastTyped = excluded.LastTyped");
            }

            foreach (var (pair, count) in bigramSnap)
            {
                await ctx.Database.ExecuteSqlInterpolatedAsync($@"
                    INSERT INTO KeyBigrams (Pair, Count) VALUES ({pair}, {count})
                    ON CONFLICT(Pair) DO UPDATE SET Count = Count + excluded.Count");
            }

            foreach (var (phrase, count) in phraseSnap)
            {
                await ctx.Database.ExecuteSqlInterpolatedAsync($@"
                    INSERT INTO WordPhrases (Phrase, Count) VALUES ({phrase}, {count})
                    ON CONFLICT(Phrase) DO UPDATE SET Count = Count + excluded.Count");
            }

            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            // Restore data on failure
            lock (_lock)
            {
                if (_captureEnabled)
                {
                    foreach (var (k, v) in wordSnap)   _words[k]   = _words.GetValueOrDefault(k)   + v;
                    foreach (var (k, v) in bigramSnap) _bigrams[k] = _bigrams.GetValueOrDefault(k) + v;
                    foreach (var (k, v) in phraseSnap) _phrases[k] = _phrases.GetValueOrDefault(k) + v;
                }
            }
        }
    }

    private void EnsureSchema()
    {
        lock (_schemaGate)
        {
            if (_schemaReady)
                return;

        using var ctx = new DataContext(_dbPath);
        ctx.Database.EnsureCreated();

        ctx.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS WordStatistics (
                Word      TEXT    PRIMARY KEY,
                Count     INTEGER NOT NULL DEFAULT 0,
                LastTyped TEXT    NOT NULL DEFAULT '',
                IsKnown   INTEGER NOT NULL DEFAULT 0
            )");
        ctx.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS KeyBigrams (
                Pair  TEXT    PRIMARY KEY,
                Count INTEGER NOT NULL DEFAULT 0
            )");
        ctx.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS WordPhrases (
                Phrase TEXT    PRIMARY KEY,
                Count  INTEGER NOT NULL DEFAULT 0
            )");
            _schemaReady = true;
        }
    }

    public async Task AnalyzeHistoryAsync()
    {
        using var timing = PerformanceLog.Measure("words.analyze");
        await _flushGate.WaitAsync();
        try
        {
            await Task.Run(() =>
            {
                EnsureSchema();
                lock (_schemaGate) CleanInvalidHistory();
            });
        }
        finally { _flushGate.Release(); }
    }

    public async Task DeleteHistoryAsync()
    {
        using var timing = PerformanceLog.Measure("words.delete");
        await _flushGate.WaitAsync();
        try
        {
            if (_captureEnabled) throw new InvalidOperationException("Disable word collection before deleting history.");
            EnsureSchema();
            await using var ctx = new DataContext(_dbPath);
            await using var tx = await ctx.Database.BeginTransactionAsync();
            await ctx.Database.ExecuteSqlRawAsync("DELETE FROM WordPhrases");
            await ctx.Database.ExecuteSqlRawAsync("DELETE FROM WordStatistics");
            await ctx.Database.ExecuteSqlRawAsync("DELETE FROM KeyBigrams");
            await tx.CommitAsync();
        }
        finally { _flushGate.Release(); }
    }

    private void CleanInvalidHistory()
    {
        using var ctx = new DataContext(_dbPath);

        var words = ctx.WordStatistics.ToList();
        var invalidWords = words.Where(w => !IsAcceptableWord(w.Word)).ToList();
        if (invalidWords.Count > 0)
            ctx.WordStatistics.RemoveRange(invalidWords);

        foreach (var word in words.Except(invalidWords))
        {
            bool isKnown = IsRecognizedWord(word.Word);
            if (word.IsKnown != isKnown)
                word.IsKnown = isKnown;
        }

        var phrases = ctx.WordPhrases.ToList();
        var invalidPhrases = phrases.Where(p => !IsAcceptablePhrase(p.Phrase)).ToList();
        if (invalidPhrases.Count > 0)
            ctx.WordPhrases.RemoveRange(invalidPhrases);

        if (invalidWords.Count > 0 || invalidPhrases.Count > 0 || ctx.ChangeTracker.HasChanges())
            ctx.SaveChanges();
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static bool IsWordBoundary(Keys key) => key is
        Keys.Space or Keys.Enter or Keys.Return or Keys.Tab or
        Keys.OemPeriod or Keys.Oemcomma or Keys.OemSemicolon or
        Keys.OemQuestion or Keys.Oem2 or Keys.OemQuotes or
        Keys.Escape or Keys.Delete or Keys.Next or Keys.Prior;

    public async Task OnApplicationExitAsync()
    {
        _timer.Stop();
        await Flush();
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
    }
}
