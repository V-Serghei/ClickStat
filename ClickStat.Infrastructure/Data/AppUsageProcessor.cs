using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using ClickStat.Infrastructure.Data.Context;
using ClickStat.Infrastructure.Data.Model;
using Microsoft.EntityFrameworkCore;
using Timer = System.Timers.Timer;

namespace ClickStat.Infrastructure.Data;

public sealed class AppUsageProcessor : IDisposable
{
    private const int FlushIntervalSeconds = 30;
    private const int WindowPollMs         = 500;

    private readonly string   _dbPath;
    private readonly Timer    _flushTimer;
    private readonly object   _lock = new();

    // In-memory buffers: exeName → (appName, keyDelta, clickDelta)
    private readonly Dictionary<string, (string name, int keys, int clicks)> _buffer = new();

    private string  _currentExe  = "unknown";
    private string  _currentName = "unknown";
    private DateTime _lastPoll   = DateTime.MinValue;

    public AppUsageProcessor()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        _dbPath  = Path.Combine(docs, "KeyClick", "key_statistics.db");

        using var ctx = new DataContext(_dbPath);
        ctx.Database.EnsureCreated();

        ctx.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS AppUsageStatistics (
                ExeName    TEXT    PRIMARY KEY,
                AppName    TEXT    NOT NULL DEFAULT '',
                KeyCount   INTEGER NOT NULL DEFAULT 0,
                ClickCount INTEGER NOT NULL DEFAULT 0
            )");

        _flushTimer = new Timer(FlushIntervalSeconds * 1000) { AutoReset = true };
        _flushTimer.Elapsed += async (_, _) => await Flush();
        _flushTimer.Start();
    }

    public void RecordKey()   => Record(isKey: true);
    public void RecordClick() => Record(isKey: false);

    private void Record(bool isKey)
    {
        PollForegroundWindowIfNeeded();
        lock (_lock)
        {
            var exe  = _currentExe;
            var name = _currentName;
            if (!_buffer.TryGetValue(exe, out var cur))
                cur = (name, 0, 0);
            _buffer[exe] = isKey
                ? (name, cur.keys + 1, cur.clicks)
                : (name, cur.keys, cur.clicks + 1);
        }
    }

    private void PollForegroundWindowIfNeeded()
    {
        if ((DateTime.Now - _lastPoll).TotalMilliseconds < WindowPollMs) return;
        _lastPoll = DateTime.Now;
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return;

            GetWindowThreadProcessId(hwnd, out uint pid);
            var proc = Process.GetProcessById((int)pid);

            _currentExe  = proc.ProcessName.ToLowerInvariant();
            _currentName = proc.MainWindowTitle.Length > 0 ? proc.MainWindowTitle : proc.ProcessName;
            if (_currentName.Length > 80) _currentName = _currentName[..80];
        }
        catch { /* process may have exited */ }
    }

    public async Task<List<AppUsageStatistics>> GetTopApps(int limit = 20)
    {
        await using var ctx = new DataContext(_dbPath);
        return await ctx.AppUsageStatistics
            .OrderByDescending(a => a.KeyCount + a.ClickCount)
            .Take(limit)
            .ToListAsync();
    }

    private async Task Flush()
    {
        Dictionary<string, (string name, int keys, int clicks)> snap;
        lock (_lock)
        {
            if (_buffer.Count == 0) return;
            snap = new Dictionary<string, (string, int, int)>(_buffer);
            _buffer.Clear();
        }

        await using var ctx = new DataContext(_dbPath);
        var tx = await ctx.Database.BeginTransactionAsync();
        try
        {
            foreach (var (exe, (name, keys, clicks)) in snap)
            {
                var row = await ctx.AppUsageStatistics.FindAsync(exe);
                if (row != null)
                {
                    row.KeyCount   += keys;
                    row.ClickCount += clicks;
                    if (name.Length > 0) row.AppName = name;
                }
                else
                    ctx.AppUsageStatistics.Add(new AppUsageStatistics
                    {
                        ExeName    = exe,
                        AppName    = name,
                        KeyCount   = keys,
                        ClickCount = clicks
                    });
            }
            await ctx.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch { await tx.RollbackAsync(); }
    }

    public async Task OnApplicationExitAsync() { _flushTimer.Stop(); await Flush(); }
    public void Dispose() { _flushTimer.Stop(); _flushTimer.Dispose(); }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}
