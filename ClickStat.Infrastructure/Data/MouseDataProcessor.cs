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

    private const int GridW = 80;  // 1920 / 24
    private const int GridH = 45;  // 1080 / 24

    private readonly string _dbPath;
    private readonly HashSet<int> _registeredCodes = new();
    private readonly Dictionary<int, (string name, long count)> _buttonBuffer = new();
    private long _scrollUpBuffer;
    private long _scrollDownBuffer;
    private long _distanceBuffer; // 0.01 mm units
    private readonly Dictionary<int, int> _heatmapBuffer = new(); // cellId → delta
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
                Id                INTEGER PRIMARY KEY,
                ScrollUpNotches   INTEGER NOT NULL DEFAULT 0,
                ScrollDownNotches INTEGER NOT NULL DEFAULT 0
            )");

        ctx.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS MouseClickCells (
                CellId INTEGER PRIMARY KEY,
                GridX  INTEGER NOT NULL DEFAULT 0,
                GridY  INTEGER NOT NULL DEFAULT 0,
                Count  INTEGER NOT NULL DEFAULT 0
            )");

        ctx.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS MouseDistances (
                Id         INTEGER PRIMARY KEY,
                TotalUnits INTEGER NOT NULL DEFAULT 0
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

    // Track actual cursor travel in screen pixels using GetCursorPos().
    // Raw RAWMOUSE lLastX/lLastY are sensor counts (device DPI, not screen pixels).
    private System.Drawing.Point _lastCursorPos;
    private bool _hasCursorPos;

    public void TrackMovement(int rawDx, int rawDy)
    {
        if (!GetCursorPos(out var pt)) return;

        if (_hasCursorPos)
        {
            int dx = pt.X - _lastCursorPos.X;
            int dy = pt.Y - _lastCursorPos.Y;
            if (dx != 0 || dy != 0)
            {
                double pixelDistance = Math.Sqrt((double)dx * dx + (double)dy * dy);
                if (pixelDistance <= GetVirtualScreenDiagonalPixels() * 1.5)
                {
                    double mm = GetMovementMillimeters(dx, dy, pt);
                    long units = (long)Math.Round(mm * 100.0); // 0.01-mm units
                    if (units > 0)
                        lock (_lock) _distanceBuffer += units;
                }
            }
        }
        _lastCursorPos = pt;
        _hasCursorPos  = true;
    }

    public void TrackClickPosition()
    {
        if (!GetCursorPos(out var pt)) return;
        var screen = SystemInformation.VirtualScreen;
        int width = Math.Max(1, screen.Width);
        int height = Math.Max(1, screen.Height);
        int gx = Math.Clamp((pt.X - screen.Left) * GridW / width, 0, GridW - 1);
        int gy = Math.Clamp((pt.Y - screen.Top) * GridH / height, 0, GridH - 1);
        int id = gx * GridH + gy;
        lock (_lock)
            _heatmapBuffer[id] = _heatmapBuffer.GetValueOrDefault(id) + 1;
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

    public async Task<long> GetTotalDistanceUnits()
    {
        await using var ctx = new DataContext(_dbPath);
        var row = await ctx.MouseDistances.FindAsync(1);
        return row?.TotalUnits ?? 0;
    }

    public async Task<List<MouseClickCell>> GetHeatmapCells()
    {
        await using var ctx = new DataContext(_dbPath);
        return await ctx.MouseClickCells.Where(c => c.Count > 0).ToListAsync();
    }

    // ──────────────────────────────────────────────
    // Persistence
    // ──────────────────────────────────────────────

    private async Task FlushToDatabase()
    {
        Dictionary<int, (string name, long count)> buttonSnapshot;
        Dictionary<int, int> heatSnap;
        long scrollUp, scrollDown, distance;

        lock (_lock)
        {
            bool empty = _buttonBuffer.Count == 0 && _scrollUpBuffer == 0
                      && _scrollDownBuffer == 0 && _distanceBuffer == 0
                      && _heatmapBuffer.Count == 0;
            if (empty) return;

            buttonSnapshot = new Dictionary<int, (string, long)>(_buttonBuffer);
            heatSnap       = new Dictionary<int, int>(_heatmapBuffer);
            scrollUp       = _scrollUpBuffer;
            scrollDown     = _scrollDownBuffer;
            distance       = _distanceBuffer;
            _buttonBuffer.Clear(); _heatmapBuffer.Clear();
            _scrollUpBuffer = 0; _scrollDownBuffer = 0; _distanceBuffer = 0;
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

            // Distance
            if (distance > 0)
            {
                var dist = await ctx.MouseDistances.FindAsync(1);
                if (dist != null) dist.TotalUnits += distance;
                else ctx.MouseDistances.Add(new MouseDistance { Id = 1, TotalUnits = distance });
            }

            // Click heatmap
            foreach (var (id, delta) in heatSnap)
            {
                var cell = await ctx.MouseClickCells.FindAsync(id);
                if (cell != null) cell.Count += delta;
                else ctx.MouseClickCells.Add(new MouseClickCell
                {
                    CellId = id,
                    GridX  = id / GridH,
                    GridY  = id % GridH,
                    Count  = delta
                });
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

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(out System.Drawing.Point lpPoint);

    private static double GetMovementMillimeters(int dx, int dy, System.Drawing.Point point)
    {
        var (dpiX, dpiY) = GetDpiForPoint(point);
        double xMm = dx * 25.4 / dpiX;
        double yMm = dy * 25.4 / dpiY;
        return Math.Sqrt(xMm * xMm + yMm * yMm);
    }

    private static (double X, double Y) GetDpiForPoint(System.Drawing.Point point)
    {
        try
        {
            var monitor = MonitorFromPoint(point, MonitorDefaultToNearest);
            if (monitor != IntPtr.Zero &&
                GetDpiForMonitor(monitor, EffectiveDpi, out uint dpiX, out uint dpiY) == 0 &&
                dpiX > 0 && dpiY > 0)
            {
                return (dpiX, dpiY);
            }
        }
        catch
        {
            // shcore.dll may be unavailable on very old Windows builds.
        }

        using var graphics = System.Drawing.Graphics.FromHwnd(IntPtr.Zero);
        return (graphics.DpiX > 0 ? graphics.DpiX : 96, graphics.DpiY > 0 ? graphics.DpiY : 96);
    }

    private static double GetVirtualScreenDiagonalPixels()
    {
        var screen = SystemInformation.VirtualScreen;
        return Math.Sqrt((double)screen.Width * screen.Width + (double)screen.Height * screen.Height);
    }

    private const uint MonitorDefaultToNearest = 2;
    private const int EffectiveDpi = 0;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(System.Drawing.Point pt, uint dwFlags);

    [System.Runtime.InteropServices.DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);
}
