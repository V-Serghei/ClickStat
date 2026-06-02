using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using ClickStat.Infrastructure.Data.Context;
using ClickStat.Infrastructure.Data.Model;
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

        _timer = new Timer(FlushIntervalSeconds * 1000) { AutoReset = true };
        _timer.Elapsed += async (_, _) => await Flush();
        _timer.Start();
    }

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>Call from MainViewModel.OnKeyReceived (KeyUp) when no Ctrl/Alt held.</summary>
    public void ProcessKey(Keys key)
    {
        lock (_lock)
        {
            if (IsLetter(key))
            {
                char c = KeyToChar(key);
                _buffer.Append(c);

                // Track bigram (key-level, raw names)
                if (_lastKey != null)
                {
                    string bigram = $"{_lastKey}|{key}";
                    _bigrams[bigram] = _bigrams.GetValueOrDefault(bigram) + 1;
                }
                _lastKey = key.ToString();
                return;
            }

            if (key == Keys.Back)
            {
                if (_buffer.Length > 0) _buffer.Remove(_buffer.Length - 1, 1);
                return;
            }

            // Word boundary
            if (IsWordBoundary(key))
            {
                CompleteWord();
            }

            _lastKey = null;
        }
    }

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
        return await ctx.WordStatistics
            .OrderByDescending(w => w.Count)
            .Take(limit)
            .ToListAsync();
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
        return await ctx.WordPhrases
            .OrderByDescending(p => p.Count)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<int> GetTotalWordsTyped()
    {
        await using var ctx = new DataContext(_dbPath);
        return (int)await ctx.WordStatistics.SumAsync(w => (long)w.Count);
    }

    public async Task<int> GetUniqueWordsCount()
    {
        await using var ctx = new DataContext(_dbPath);
        return await ctx.WordStatistics.CountAsync();
    }

    // ── Internal ──────────────────────────────────────────────────────────

    private void CompleteWord()
    {
        if (_buffer.Length < MinWordLength) { _buffer.Clear(); return; }

        string raw  = _buffer.ToString();
        string word = raw.ToLowerInvariant();
        _buffer.Clear();

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
                }
                else
                    ctx.WordStatistics.Add(new WordStatistics
                    {
                        Word      = word,
                        Count     = count,
                        LastTyped = DateTime.Now,
                        IsKnown   = EnglishDictionary.Contains(word)
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

    // ── Helpers ───────────────────────────────────────────────────────────

    private static bool IsLetter(Keys key) =>
        key is >= Keys.A and <= Keys.Z;

    private static bool IsWordBoundary(Keys key) => key is
        Keys.Space or Keys.Enter or Keys.Return or Keys.Tab or
        Keys.OemPeriod or Keys.Oemcomma or Keys.OemSemicolon or
        Keys.OemQuestion or Keys.Oem2 or Keys.OemQuotes or
        Keys.Escape or Keys.Delete or Keys.Next or Keys.Prior;

    private static char KeyToChar(Keys key) =>
        (char)('a' + (key - Keys.A));

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
