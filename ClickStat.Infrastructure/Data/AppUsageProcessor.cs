using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using ClickStat.Infrastructure.Data.Context;
using ClickStat.Infrastructure.Data.Model;
using Microsoft.EntityFrameworkCore;
using Timer = System.Timers.Timer;

namespace ClickStat.Infrastructure.Data;

public sealed class AppUsageProcessor : IDisposable
{
    private const int FlushIntervalSeconds = 30;
    private const int WindowPollMs = 500;

    private readonly string _dbPath;
    private readonly Timer _flushTimer;
    private readonly object _lock = new();

    private readonly Dictionary<string, (string name, int keys, int clicks)> _buffer = new();
    private readonly Dictionary<string, string> _nameCache = new();

    private string _currentExe = "unknown.exe";
    private string _currentName = "Unknown";
    private DateTime _lastPoll = DateTime.MinValue;

    private static readonly Dictionary<string, string> KnownAppNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["brave.exe"] = "Brave",
        ["chrome.exe"] = "Google Chrome",
        ["code.exe"] = "Visual Studio Code",
        ["codex.exe"] = "Codex",
        ["clickstat.app.exe"] = "ClickStat",
        ["explorer.exe"] = "File Explorer",
        ["livecaptionstranslator.exe"] = "Live Captions Translator",
        ["ms-teams.exe"] = "Microsoft Teams",
        ["mstsc.exe"] = "Remote Desktop Connection",
        ["rider64.exe"] = "JetBrains Rider",
        ["telegram.exe"] = "Telegram Desktop",
        ["windowsterminal.exe"] = "Windows Terminal"
    };

    public AppUsageProcessor()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        _dbPath = Path.Combine(docs, "KeyClick", "key_statistics.db");

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

    public void RecordKey() => Record(isKey: true);
    public void RecordClick() => Record(isKey: false);

    private void Record(bool isKey)
    {
        PollForegroundWindowIfNeeded();
        lock (_lock)
        {
            var exe = _currentExe;
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

            string exePath = "";
            try
            {
                exePath = proc.MainModule?.FileName ?? "";
            }
            catch
            {
                // Protected/system processes may deny MainModule access.
            }

            string exeName = Path.GetFileName(exePath);
            if (string.IsNullOrWhiteSpace(exeName))
                exeName = proc.ProcessName;

            var identity = ResolveAppIdentity(exeName, exePath, proc.ProcessName);
            _currentExe = identity.ExeKey;
            _currentName = identity.DisplayName;
        }
        catch
        {
            // The foreground process may exit while being inspected.
        }
    }

    public async Task<List<AppUsageStatistics>> GetTopApps(int limit = 20)
    {
        await using var ctx = new DataContext(_dbPath);
        var rows = await ctx.AppUsageStatistics.ToListAsync();

        return rows
            .GroupBy(row => NormalizeExeKey(row.ExeName))
            .Select(group =>
            {
                var key = group.Key;
                return new AppUsageStatistics
                {
                    ExeName = key,
                    AppName = ResolveDisplayNameForRows(key, group),
                    KeyCount = group.Sum(row => row.KeyCount),
                    ClickCount = group.Sum(row => row.ClickCount)
                };
            })
            .OrderByDescending(app => app.KeyCount + app.ClickCount)
            .Take(limit)
            .ToList();
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
                var key = NormalizeExeKey(exe);
                var displayName = ResolveDisplayNameForKey(key, name);
                var row = await ctx.AppUsageStatistics.FindAsync(key);
                if (row != null)
                {
                    row.KeyCount += keys;
                    row.ClickCount += clicks;
                    if (displayName.Length > 0) row.AppName = displayName;
                }
                else
                {
                    ctx.AppUsageStatistics.Add(new AppUsageStatistics
                    {
                        ExeName = key,
                        AppName = displayName,
                        KeyCount = keys,
                        ClickCount = clicks
                    });
                }
            }

            await ctx.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
        }
    }

    public async Task OnApplicationExitAsync()
    {
        _flushTimer.Stop();
        await Flush();
    }

    public void Dispose()
    {
        _flushTimer.Stop();
        _flushTimer.Dispose();
    }

    private AppIdentity ResolveAppIdentity(string exeName, string exePath, string processName)
    {
        var key = NormalizeExeKey(exeName);
        var fallback = string.IsNullOrWhiteSpace(processName) ? key : processName;

        if (KnownAppNames.TryGetValue(key, out var known))
            return new AppIdentity(key, known);

        var displayName = ResolveDisplayNameForKey(key, fallback);
        if (string.IsNullOrWhiteSpace(exePath))
            return new AppIdentity(key, displayName);

        if (_nameCache.TryGetValue(exePath, out var cached))
            return new AppIdentity(key, ResolveDisplayNameForKey(key, cached));

        try
        {
            var vi = FileVersionInfo.GetVersionInfo(exePath);
            var versionName = PickBetterVersionName(vi.FileDescription, vi.ProductName);
            if (!string.IsNullOrWhiteSpace(versionName))
                displayName = ResolveDisplayNameForKey(key, versionName);
        }
        catch
        {
            // Keep the process-name fallback.
        }

        _nameCache[exePath] = displayName;
        return new AppIdentity(key, displayName);
    }

    private static string ResolveDisplayNameForRows(string key, IEnumerable<AppUsageStatistics> rows)
    {
        if (KnownAppNames.TryGetValue(key, out var known))
            return known;

        var stableName = rows
            .Select(row => row.AppName?.Trim())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Where(name => !IsLikelyWindowTitle(name!))
            .OrderBy(name => name!.Length)
            .FirstOrDefault();

        return ResolveDisplayNameForKey(key, stableName ?? "");
    }

    private static string ResolveDisplayNameForKey(string key, string candidate)
    {
        if (KnownAppNames.TryGetValue(key, out var known))
            return known;

        candidate = candidate.Trim();
        if (!string.IsNullOrWhiteSpace(candidate) &&
            !IsGenericWindowsProductName(candidate) &&
            !IsLikelyWindowTitle(candidate))
        {
            return candidate;
        }

        return HumanizeExeName(key);
    }

    private static string? PickBetterVersionName(string? fileDescription, string? productName)
    {
        var description = fileDescription?.Trim();
        if (!string.IsNullOrWhiteSpace(description) && !IsGenericWindowsProductName(description))
            return description;

        var product = productName?.Trim();
        if (!string.IsNullOrWhiteSpace(product) && !IsGenericWindowsProductName(product))
            return product;

        return null;
    }

    private static string NormalizeExeKey(string value)
    {
        var fileName = Path.GetFileName(value.Trim()).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(fileName))
            return "unknown.exe";

        return fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? fileName
            : $"{fileName}.exe";
    }

    private static bool IsGenericWindowsProductName(string value) =>
        value.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) &&
        value.Contains("Windows", StringComparison.OrdinalIgnoreCase) &&
        value.Contains("Operating System", StringComparison.OrdinalIgnoreCase);

    private static bool IsLikelyWindowTitle(string value) =>
        value.Contains(" - ", StringComparison.Ordinal) ||
        value.Contains(" — ", StringComparison.Ordinal) ||
        value.Contains(" – ", StringComparison.Ordinal) ||
        value.Contains(" | ", StringComparison.Ordinal);

    private static string HumanizeExeName(string key)
    {
        var name = Path.GetFileNameWithoutExtension(key);
        if (string.IsNullOrWhiteSpace(name))
            return "Unknown";

        return string.Join(
            " ",
            name.Split(new[] { '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    }

    private sealed record AppIdentity(string ExeKey, string DisplayName);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}
