using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using ClickStat.Infrastructure.Data.Context;
using ClickStat.Infrastructure.Data.Model;
using Microsoft.EntityFrameworkCore;
using Timer = System.Timers.Timer;

namespace ClickStat.Infrastructure.Data;

public class MouseDataProcessor : IDisposable
{
    private const int SaveIntervalSeconds = 5;

    // Standard buttons pre-registered on startup
    private static readonly Dictionary<int, string> DefaultButtons = new()
    {
        { (int)MouseButtons.Left,     "Левая кнопка" },
        { (int)MouseButtons.Right,    "Правая кнопка" },
        { (int)MouseButtons.Middle,   "Колесо (клик)" },
        { (int)MouseButtons.XButton1, "Кнопка назад" },
        { (int)MouseButtons.XButton2, "Кнопка вперёд" },
    };

    private readonly string _dbPath;
    private readonly HashSet<int> _registeredCodes = new();
    private readonly Dictionary<int, (string name, long count)> _buttonBuffer = new();
    private long _scrollUpBuffer;
    private long _scrollDownBuffer;
    private readonly object _lock = new();
    private readonly Timer _saveTimer;

    public MouseDataProcessor()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var folder = Path.Combine(docs, "KeyClick");
        Directory.CreateDirectory(folder);
        _dbPath = Path.Combine(folder, "key_statistics.db");

        EnsureSchema();
        EnsureDefaultButtons();
        LoadRegisteredCodes();

        _saveTimer = new Timer(SaveIntervalSeconds * 1000) { AutoReset = true };
        _saveTimer.Elapsed += async (_, _) => await FlushToDatabase();
        _saveTimer.Start();
    }

    // ──────────────────────────────────────────────
    // Schema helpers (safe for existing DBs)
    // ──────────────────────────────────────────────

    private void EnsureSchema()
    {
        using var ctx = new DataContext(_dbPath);
        ctx.Database.EnsureCreated();

        // Add IsRegistered column if the table existed before this feature
        try
        {
            ctx.Database.ExecuteSqlRaw(
                "ALTER TABLE MouseStatistics ADD COLUMN IsRegistered INTEGER NOT NULL DEFAULT 1");
        }
        catch { /* column already exists — ignore */ }

        // Create scroll table if it didn't exist yet
        ctx.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS MouseScrollStatistics (
                Id INTEGER PRIMARY KEY,
                ScrollUpNotches INTEGER NOT NULL DEFAULT 0,
                ScrollDownNotches INTEGER NOT NULL DEFAULT 0
            )");
    }

    private void EnsureDefaultButtons()
    {
        using var ctx = new DataContext(_dbPath);

        foreach (var (code, name) in DefaultButtons)
        {
            if (ctx.MouseStatistics.Find(code) == null)
            {
                ctx.MouseStatistics.Add(new MouseStatistics
                {
                    ButtonCode = code,
                    ButtonName = name,
                    Count = 0,
                    IsRegistered = true
                });
            }
        }

        if (ctx.MouseScrollStatistics.Find(1) == null)
        {
            ctx.MouseScrollStatistics.Add(new MouseScrollStatistics { Id = 1 });
        }

        ctx.SaveChanges();
    }

    private void LoadRegisteredCodes()
    {
        using var ctx = new DataContext(_dbPath);
        foreach (var b in ctx.MouseStatistics.Where(b => b.IsRegistered))
            _registeredCodes.Add(b.ButtonCode);
    }

    // ──────────────────────────────────────────────
    // Runtime tracking
    // ──────────────────────────────────────────────

    public bool IsRegistered(int buttonCode)
    {
        lock (_lock) return _registeredCodes.Contains(buttonCode);
    }

    public void TrackButtonClick(int buttonCode, string buttonName)
    {
        lock (_lock)
        {
            if (_buttonBuffer.TryGetValue(buttonCode, out var existing))
                _buttonBuffer[buttonCode] = (existing.name, existing.count + 1);
            else
                _buttonBuffer[buttonCode] = (buttonName, 1);
        }
    }

    public void TrackScroll(int notches)
    {
        lock (_lock)
        {
            if (notches > 0) _scrollUpBuffer += notches;
            else _scrollDownBuffer += Math.Abs(notches);
        }
    }

    // ──────────────────────────────────────────────
    // Custom button registration
    // ──────────────────────────────────────────────

    public async Task RegisterCustomButton(int buttonCode, string buttonName)
    {
        await using var ctx = new DataContext(_dbPath);
        var existing = await ctx.MouseStatistics.FindAsync(buttonCode);
        if (existing != null)
        {
            existing.ButtonName = buttonName;
            existing.IsRegistered = true;
        }
        else
        {
            ctx.MouseStatistics.Add(new MouseStatistics
            {
                ButtonCode = buttonCode,
                ButtonName = buttonName,
                Count = 0,
                IsRegistered = true
            });
        }
        await ctx.SaveChangesAsync();

        lock (_lock) _registeredCodes.Add(buttonCode);
    }

    // ──────────────────────────────────────────────
    // Read
    // ──────────────────────────────────────────────

    public async Task<List<MouseStatistics>> GetButtonStatistics()
    {
        await using var ctx = new DataContext(_dbPath);
        return await ctx.MouseStatistics.ToListAsync();
    }

    public async Task<MouseScrollStatistics?> GetScrollStatistics()
    {
        await using var ctx = new DataContext(_dbPath);
        return await ctx.MouseScrollStatistics.FindAsync(1);
    }

    // ──────────────────────────────────────────────
    // Persistence
    // ──────────────────────────────────────────────

    private async Task FlushToDatabase()
    {
        Dictionary<int, (string name, long count)> buttonSnapshot;
        long scrollUp, scrollDown;

        lock (_lock)
        {
            if (_buttonBuffer.Count == 0 && _scrollUpBuffer == 0 && _scrollDownBuffer == 0)
                return;

            buttonSnapshot = new Dictionary<int, (string, long)>(_buttonBuffer);
            scrollUp = _scrollUpBuffer;
            scrollDown = _scrollDownBuffer;
            _buttonBuffer.Clear();
            _scrollUpBuffer = 0;
            _scrollDownBuffer = 0;
        }

        await using var ctx = new DataContext(_dbPath);
        var tx = await ctx.Database.BeginTransactionAsync();
        try
        {
            foreach (var (code, (name, count)) in buttonSnapshot)
            {
                var stat = await ctx.MouseStatistics.FindAsync(code);
                if (stat != null)
                    stat.Count += count;
                else
                    ctx.MouseStatistics.Add(new MouseStatistics
                    {
                        ButtonCode = code,
                        ButtonName = name,
                        Count = count,
                        IsRegistered = false
                    });
            }

            if (scrollUp > 0 || scrollDown > 0)
            {
                var scroll = await ctx.MouseScrollStatistics.FindAsync(1);
                if (scroll != null)
                {
                    scroll.ScrollUpNotches += scrollUp;
                    scroll.ScrollDownNotches += scrollDown;
                }
            }

            await ctx.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            Console.WriteLine($"Mouse flush failed: {ex.Message}");
        }
    }

    public async Task OnApplicationExitAsync()
    {
        _saveTimer.Stop();
        await FlushToDatabase();
    }

    public void Dispose()
    {
        _saveTimer.Stop();
        _saveTimer.Dispose();
    }
}
