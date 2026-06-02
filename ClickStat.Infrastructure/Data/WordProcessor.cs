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
using ClickStat.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Timer = System.Timers.Timer;

namespace ClickStat.Infrastructure.Data;

/// <summary>
/// Reconstructs words from individual KeyUp events, tracks frequencies,
/// bigrams, and 2-word phrases. Flushes to DB every 30 s.
/// </summary>
public sealed class WordProcessor : IDisposable
{
    private const int FlushIntervalSeconds = 30;
    private const int MinWordLength        = 2;
    private const int MaxWordLength        = 32;

    private readonly string _dbPath;
    private readonly Timer  _timer;
    private readonly object _lock = new();

    // In-memory buffers
    private readonly StringBuilder              _buffer   = new();
    private readonly Queue<string>              _lastWords = new(3); // sliding window for phrases
    private string?                             _lastKey;            // for bigrams
    private readonly Dictionary<string, int>   _words    = new();
    private readonly Dictionary<string, int>   _bigrams  = new();
    private readonly Dictionary<string, int>   _phrases  = new();

    public WordProcessor()
    {
        var docs   = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        _dbPath    = Path.Combine(docs, "KeyClick", "key_statistics.db");

        EnsureSchema();
        CleanInvalidHistory();

        _timer = new Timer(FlushIntervalSeconds * 1000) { AutoReset = true };
        _timer.Elapsed += async (_, _) => await Flush();
        _timer.Start();
    }

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Call from MainViewModel.OnKeyReceived (KeyUp) when no Ctrl/Alt held.
    /// Uses ToUnicode to get the actual character for ANY keyboard layout (RU, EN, DE, …).
    /// </summary>
    public void ProcessKey(Keys key)
    {
        lock (_lock)
        {
            if (key == Keys.Back)
            {
                if (_buffer.Length > 0) _buffer.Remove(_buffer.Length - 1, 1);
                return;
            }

            // Try to resolve the actual unicode character for current layout + modifier state
            char? ch = ResolveChar(key);

            if (ch.HasValue && char.IsLetter(ch.Value))
            {
                _buffer.Append(char.ToLowerInvariant(ch.Value));

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

            _lastKey = null;
        }
    }

    /// <summary>
    /// Converts a virtual key to the character produced in the current keyboard layout.
    /// Uses flag 4 (peek-only) to avoid consuming dead key state.
    /// </summary>
    private static char? ResolveChar(Keys key)
    {
        var keyState = new byte[256];
        if (!GetKeyboardState(keyState)) return null;

        var sb = new StringBuilder(4);
        var layout = LayoutService.GetCurrentKeyboardLayoutHandle();
        uint scanCode = (uint)MapVirtualKeyEx((uint)key, 0, layout);
        int result = ToUnicodeEx((uint)key, scanCode, keyState, sb, 4, 4, layout);
        if (result > 0 && sb.Length > 0)
            return sb[0];
        return null;
    }

    [DllImport("user32.dll")]
    private static extern bool GetKeyboardState(byte[] lpKeyState);

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

    public void ClearBuffer()
    {
        lock (_lock)
        {
            _buffer.Clear();
            _lastKey = null;
        }
    }

    // ── DB data access ────────────────────────────────────────────────────

    public async Task<List<WordStatistics>> GetTopWords(int limit = 50)
    {
        await using var ctx = new DataContext(_dbPath);
        var words = await ctx.WordStatistics
            .OrderByDescending(w => w.Count)
            .ToListAsync();

        return words
            .Where(w => IsAcceptableWord(w.Word))
            .Take(limit)
            .ToList();
    }

    public async Task<List<KeyBigram>> GetTopBigrams(int limit = 30)
    {
        await using var ctx = new DataContext(_dbPath);
        return await ctx.KeyBigrams
            .OrderByDescending(b => b.Count)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<List<WordPhrase>> GetTopPhrases(int limit = 30)
    {
        await using var ctx = new DataContext(_dbPath);
        var phrases = await ctx.WordPhrases
            .OrderByDescending(p => p.Count)
            .ToListAsync();

        return phrases
            .Where(p => IsAcceptablePhrase(p.Phrase))
            .Take(limit)
            .ToList();
    }

    public async Task<int> GetTotalWordsTyped()
    {
        await using var ctx = new DataContext(_dbPath);
        var words = await ctx.WordStatistics.ToListAsync();
        return words.Where(w => IsAcceptableWord(w.Word)).Sum(w => w.Count);
    }

    public async Task<int> GetUniqueWordsCount()
    {
        await using var ctx = new DataContext(_dbPath);
        var words = await ctx.WordStatistics.ToListAsync();
        return words.Count(w => IsAcceptableWord(w.Word));
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

    private static bool IsKnownEnglishWord(string word)
    {
        // Latin keyboard garbage is common, so English uses a dictionary gate.
        return EnglishDictionary.Contains(word);
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
        if (_buffer.Length < MinWordLength) { _buffer.Clear(); return; }

        string raw  = _buffer.ToString();
        string word = raw.ToLowerInvariant();
        _buffer.Clear();

        if (!IsAcceptableWord(word))
        {
            _lastWords.Clear();
            return;
        }

        string? language = GetWordLanguage(word);
        if (_lastWords.Count > 0 && GetWordLanguage(_lastWords.Last()) != language)
            _lastWords.Clear();

        _words[word] = _words.GetValueOrDefault(word) + 1;

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

    private async Task Flush()
    {
        Dictionary<string, int> wordSnap, bigramSnap, phraseSnap;
        lock (_lock)
        {
            if (_words.Count == 0 && _bigrams.Count == 0 && _phrases.Count == 0) return;
            wordSnap   = new Dictionary<string, int>(_words);
            bigramSnap = new Dictionary<string, int>(_bigrams);
            phraseSnap = new Dictionary<string, int>(_phrases);
            _words.Clear(); _bigrams.Clear(); _phrases.Clear();
        }

        await using var ctx = new DataContext(_dbPath);
        var tx = await ctx.Database.BeginTransactionAsync();
        try
        {
            foreach (var (word, count) in wordSnap)
            {
                var existing = await ctx.WordStatistics.FindAsync(word);
                if (existing != null)
                {
                    existing.Count   += count;
                    existing.LastTyped = DateTime.Now;
                    existing.IsKnown = IsRecognizedWord(word);
                }
                else
                    ctx.WordStatistics.Add(new WordStatistics
                    {
                        Word      = word,
                        Count     = count,
                        LastTyped = DateTime.Now,
                        IsKnown   = IsRecognizedWord(word)
                    });
            }

            foreach (var (pair, count) in bigramSnap)
            {
                var existing = await ctx.KeyBigrams.FindAsync(pair);
                if (existing != null) existing.Count += count;
                else ctx.KeyBigrams.Add(new KeyBigram { Pair = pair, Count = count });
            }

            foreach (var (phrase, count) in phraseSnap)
            {
                var existing = await ctx.WordPhrases.FindAsync(phrase);
                if (existing != null) existing.Count += count;
                else ctx.WordPhrases.Add(new WordPhrase { Phrase = phrase, Count = count });
            }

            await ctx.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            // Restore data on failure
            lock (_lock)
            {
                foreach (var (k, v) in wordSnap)   _words[k]   = _words.GetValueOrDefault(k)   + v;
                foreach (var (k, v) in bigramSnap) _bigrams[k] = _bigrams.GetValueOrDefault(k) + v;
                foreach (var (k, v) in phraseSnap) _phrases[k] = _phrases.GetValueOrDefault(k) + v;
            }
        }
    }

    private void EnsureSchema()
    {
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
