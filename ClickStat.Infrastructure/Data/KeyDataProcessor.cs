using System.IO;
using System.Windows.Forms;
using ClickStat.Infrastructure.Data.Context;
using ClickStat.Infrastructure.Data.Model;
using Microsoft.EntityFrameworkCore;
using Timer = System.Timers.Timer;

namespace ClickStat.Infrastructure.Data;

public class KeyDataProcessor
{
    // 5 s matches MouseDataProcessor; 1 s was overkill and caused
    // a DB transaction every second even with no keypresses.
    private const int SaveIntervalSeconds = 5;
    private const int MaxDelaySeconds = 20;

    private readonly string _dbPath;
    private readonly Dictionary<Keys, KeyStatistics> _keyStatistics = new();
    private readonly object _lock = new();
    private readonly Timer _saveTimer;
    private DateTime _lastSaveTime = DateTime.MinValue;

    // Ensures only one flush runs at a time — prevents race between auto-timer
    // and manual FlushAsync() called before a DB read.
    private readonly SemaphoreSlim _flushGate = new(1, 1);

    public KeyDataProcessor()
    {
        var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var folderPath    = Path.Combine(documentsPath, "KeyClick");
        Directory.CreateDirectory(folderPath);

        _dbPath = Path.Combine(folderPath, "key_statistics.db");

        using var ctx = new DataContext(_dbPath);
        ctx.Database.EnsureCreated();

        _saveTimer = new Timer(SaveIntervalSeconds * 1000) { AutoReset = true };
        _saveTimer.Elapsed += async (_, _) => await SaveToDatabaseBuffered();
        _saveTimer.Start();
    }

    public Task ProcessKeyPress(Keys key)
    {
        if (key == Keys.None) return Task.CompletedTask;

        lock (_lock)
        {
            if (_keyStatistics.TryGetValue(key, out var existing))
                existing.Count++;
            else
                _keyStatistics[key] = new KeyStatistics { KeyCode = (int)key, KeyName = key.ToString(), Count = 1 };
        }

        // Emergency flush if data has been accumulating for too long without a save
        if ((DateTime.Now - _lastSaveTime).TotalSeconds >= MaxDelaySeconds)
            _ = SaveToDatabaseBuffered();

        return Task.CompletedTask;
    }

    private async Task SaveToDatabaseBuffered()
    {
        // Gate ensures only one flush runs at a time.
        // Manual FlushAsync() will WAIT here if the auto-timer is currently writing,
        // so the subsequent DB read always sees committed data.
        await _flushGate.WaitAsync();
        try
        {
            await SaveToDatabaseBufferedCore();
        }
        finally
        {
            _flushGate.Release();
        }
    }

    private async Task SaveToDatabaseBufferedCore()
    {
        // Snapshot under lock — prevents race between timer flush and keypress
        Dictionary<Keys, KeyStatistics> snapshot;
        lock (_lock)
        {
            if (_keyStatistics.Count == 0) return;  // Nothing accumulated — skip DB entirely
            snapshot = new Dictionary<Keys, KeyStatistics>(_keyStatistics);
            _keyStatistics.Clear();
        }

        await using var context = new DataContext(_dbPath);
        var transaction = await context.Database.BeginTransactionAsync();
        try
        {
            var totalCount = 0;
            foreach (var (_, stat) in snapshot)
            {
                var existing = await context.KeyStatistics.FindAsync(stat.KeyCode);
                if (existing != null)
                    existing.Count += stat.Count;
                else
                    context.KeyStatistics.Add(new KeyStatistics
                    {
                        KeyCode = stat.KeyCode,
                        KeyName = stat.KeyName,
                        Count   = stat.Count
                    });
                totalCount += stat.Count;
            }

            var today = await context.KeyStatisticsForTheDay
                .FirstOrDefaultAsync(k => k.Date.Date == DateTime.Now.Date);
            if (today != null)
                today.ClickCount += totalCount;
            else
                context.KeyStatisticsForTheDay.Add(new KeyStatisticsForTheDay
                {
                    Date       = DateTime.Now.Date,
                    ClickCount = totalCount
                });

            await context.SaveChangesAsync();
            await transaction.CommitAsync();
            _lastSaveTime = DateTime.Now;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            Console.WriteLine($"Save failed: {ex.Message}");

            // Put data back so it isn't lost
            lock (_lock)
            {
                foreach (var (key, stat) in snapshot)
                {
                    if (_keyStatistics.TryGetValue(key, out var existing))
                        existing.Count += stat.Count;
                    else
                        _keyStatistics[key] = stat;
                }
            }
        }
    }

    /// <summary>Force-flush the in-memory buffer to DB immediately (e.g. before reading fresh data).</summary>
    public Task FlushAsync() => SaveToDatabaseBuffered();

    public async Task OnApplicationExitAsync()
    {
        _saveTimer.Stop();
        await SaveToDatabaseBuffered();
    }
}
