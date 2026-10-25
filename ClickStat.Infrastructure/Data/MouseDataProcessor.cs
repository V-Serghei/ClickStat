using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using ClickStat.Infrastructure.Data.Context;
using ClickStat.Infrastructure.Data.Model;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Timer = System.Timers.Timer;

namespace ClickStat.Infrastructure.Data;

public class MouseDataProcessor : IDisposable
{
    private const int SaveIntervalSeconds = 5;

    // Standard buttons pre-registered on startup
    private static readonly Dictionary<int, string> DefaultButtons = new()
    {
        { (int)MouseButtons.Left,     "Left button" },
        { (int)MouseButtons.Right,    "Right button" },
        { (int)MouseButtons.Middle,   "Middle button" },
        { (int)MouseButtons.XButton1, "Back button" },
        { (int)MouseButtons.XButton2, "Forward button" },
    };

    private const int GridW = 80;  // 1920 / 24
    private const int GridH = 45;  // 1080 / 24
    private const int CursorGridW = 80;
    private const int CursorGridH = 45;

    private readonly string _dbPath;
    private readonly HashSet<int> _registeredCodes = new();
    private readonly Dictionary<int, (string name, long count)> _buttonBuffer = new();
    private long _scrollUpBuffer;
    private long _scrollDownBuffer;
    private long _distanceBuffer; // 0.01 mm units
    private readonly Dictionary<int, int> _heatmapBuffer = new(); // cellId → delta
    private readonly Dictionary<(string layout, int id), long> _cursorHeatmapBuffer = new();
    private readonly Dictionary<(string layout, int id), long> _cursorMovementBuffer = new();
    private long _lastMovementSampleTick;
    private (string layout, int x, int y)? _lastMovementCell;
    private readonly object _lock = new();
    private readonly Timer _saveTimer;
    private readonly Timer _cursorSampleTimer;
    private readonly SemaphoreSlim _flushGate = new(1, 1);

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
        _cursorSampleTimer = new Timer(1000) { AutoReset = true };
        _cursorSampleTimer.Elapsed += (_, _) => SampleCursorPosition();
        _cursorSampleTimer.Start();
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

        ctx.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS CursorHeatmapCells (
                LayoutKey TEXT NOT NULL,
                CellId INTEGER NOT NULL,
                Seconds INTEGER NOT NULL DEFAULT 0,
                Visits INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (LayoutKey, CellId)
            )");
        try { ctx.Database.ExecuteSqlRaw("ALTER TABLE CursorHeatmapCells ADD COLUMN Visits INTEGER NOT NULL DEFAULT 0"); }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 1 && ex.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase)) { }
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

    // Called from the mouse monitor's low-rate worker, never from the system hook.
    public void TrackMovement(long distanceUnits, int cursorX, int cursorY)
    {
        if (distanceUnits > 0) lock (_lock) _distanceBuffer += distanceUnits;
        var now = Environment.TickCount64;
        var previousTick = Interlocked.Read(ref _lastMovementSampleTick);
        if (now - previousTick < 250) return;
        Interlocked.Exchange(ref _lastMovementSampleTick, now);
        var bounds = SystemInformation.VirtualScreen;
        if (bounds.Width <= 0 || bounds.Height <= 0 || !bounds.Contains(cursorX, cursorY)) return;
        var layout = LayoutKey(bounds);
        int x = Math.Clamp((int)((long)(cursorX - bounds.Left) * CursorGridW / bounds.Width), 0, CursorGridW - 1);
        int y = Math.Clamp((int)((long)(cursorY - bounds.Top) * CursorGridH / bounds.Height), 0, CursorGridH - 1);
        lock (_lock)
        {
            AddMovementCell(layout, x, y);
            if (now - previousTick <= 1000 && _lastMovementCell is { } previous && previous.layout == layout)
                AddMovementCell(layout, (x + previous.x) / 2, (y + previous.y) / 2);
            _lastMovementCell = (layout, x, y);
        }
    }

    private void AddMovementCell(string layout, int x, int y)
    {
        var key = (layout, x * CursorGridH + y);
        _cursorMovementBuffer[key] = _cursorMovementBuffer.GetValueOrDefault(key) + 1;
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

    private void SampleCursorPosition()
    {
        if (!GetCursorPos(out var point)) return;
        var bounds = SystemInformation.VirtualScreen;
        if (bounds.Width <= 0 || bounds.Height <= 0 || !bounds.Contains(point)) return;
        var layout = LayoutKey(bounds);
        int x = Math.Clamp((int)((long)(point.X - bounds.Left) * CursorGridW / bounds.Width), 0, CursorGridW - 1);
        int y = Math.Clamp((int)((long)(point.Y - bounds.Top) * CursorGridH / bounds.Height), 0, CursorGridH - 1);
        lock (_lock)
        {
            var key = (layout, x * CursorGridH + y);
            _cursorHeatmapBuffer[key] = _cursorHeatmapBuffer.GetValueOrDefault(key) + 1;
        }
    }

    private static string LayoutKey(System.Drawing.Rectangle bounds)
    {
        var screens = Screen.AllScreens
            .OrderBy(screen => screen.DeviceName, StringComparer.Ordinal)
            .Select(screen => $"{screen.DeviceName}:{screen.Bounds.Left}:{screen.Bounds.Top}:{screen.Bounds.Width}:{screen.Bounds.Height}");
        return $"{bounds.Left}:{bounds.Top}:{bounds.Width}:{bounds.Height}|{string.Join('|', screens)}";
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

    public async Task<CursorHeatmap> GetCursorHeatmapAsync()
    {
        var bounds = SystemInformation.VirtualScreen;
        var cells = new List<CursorHeatmapCell>();
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath, Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared, DefaultTimeout = 5
        }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CellId, Seconds, Visits FROM CursorHeatmapCells WHERE LayoutKey = $layout";
        command.Parameters.AddWithValue("$layout", LayoutKey(bounds));
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            int id = reader.GetInt32(0);
            cells.Add(new CursorHeatmapCell(id / CursorGridH, id % CursorGridH, reader.GetInt64(1), reader.GetInt64(2)));
        }
        return new CursorHeatmap(bounds.Left, bounds.Top, bounds.Width, bounds.Height, cells);
    }

    // ──────────────────────────────────────────────
    // Persistence
    // ──────────────────────────────────────────────

    private async Task FlushToDatabase()
    {
        using var timing = ClickStat.Infrastructure.Diagnostics.PerformanceLog.Measure("mouse.flush");
        await _flushGate.WaitAsync();
        try { await FlushToDatabaseCore(); }
        finally { _flushGate.Release(); }
    }

    private async Task FlushToDatabaseCore()
    {
        Dictionary<int, (string name, long count)> buttonSnapshot;
        Dictionary<int, int> heatSnap;
        Dictionary<(string layout, int id), long> cursorSnap;
        Dictionary<(string layout, int id), long> movementSnap;
        long scrollUp, scrollDown, distance;

        lock (_lock)
        {
            bool empty = _buttonBuffer.Count == 0 && _scrollUpBuffer == 0
                      && _scrollDownBuffer == 0 && _distanceBuffer == 0
                      && _heatmapBuffer.Count == 0;
            empty = empty && _cursorHeatmapBuffer.Count == 0 && _cursorMovementBuffer.Count == 0;
            if (empty) return;

            buttonSnapshot = new Dictionary<int, (string, long)>(_buttonBuffer);
            heatSnap       = new Dictionary<int, int>(_heatmapBuffer);
            cursorSnap     = new Dictionary<(string layout, int id), long>(_cursorHeatmapBuffer);
            movementSnap   = new Dictionary<(string layout, int id), long>(_cursorMovementBuffer);
            scrollUp       = _scrollUpBuffer;
            scrollDown     = _scrollDownBuffer;
            distance       = _distanceBuffer;
            _buttonBuffer.Clear(); _heatmapBuffer.Clear();
            _cursorHeatmapBuffer.Clear();
            _cursorMovementBuffer.Clear();
            _scrollUpBuffer = 0; _scrollDownBuffer = 0; _distanceBuffer = 0;
        }

        try
        {
            await using var ctx = new DataContext(_dbPath);
            await using var tx = await ctx.Database.BeginTransactionAsync();
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

            foreach (var key in cursorSnap.Keys.Union(movementSnap.Keys))
            {
                var (layout, id) = key;
                var seconds = cursorSnap.GetValueOrDefault(key);
                var visits = movementSnap.GetValueOrDefault(key);
                await ctx.Database.ExecuteSqlRawAsync("""
                    INSERT INTO CursorHeatmapCells (LayoutKey, CellId, Seconds, Visits)
                    VALUES ({0}, {1}, {2}, {3})
                    ON CONFLICT(LayoutKey, CellId) DO UPDATE SET
                        Seconds = Seconds + excluded.Seconds,
                        Visits = Visits + excluded.Visits
                    """, layout, id, seconds, visits);
            }

            await ctx.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Mouse flush failed: {ex.Message}");
            lock (_lock)
            {
                foreach (var (code, (name, count)) in buttonSnapshot)
                {
                    var current = _buttonBuffer.GetValueOrDefault(code);
                    _buttonBuffer[code] = (current.name ?? name, current.count + count);
                }
                foreach (var (id, delta) in heatSnap)
                    _heatmapBuffer[id] = _heatmapBuffer.GetValueOrDefault(id) + delta;
                foreach (var (key, seconds) in cursorSnap)
                    _cursorHeatmapBuffer[key] = _cursorHeatmapBuffer.GetValueOrDefault(key) + seconds;
                foreach (var (key, visits) in movementSnap)
                    _cursorMovementBuffer[key] = _cursorMovementBuffer.GetValueOrDefault(key) + visits;
                _scrollUpBuffer += scrollUp;
                _scrollDownBuffer += scrollDown;
                _distanceBuffer += distance;
            }
        }
    }

    /// <summary>Force-flush the in-memory buffer to DB immediately (e.g. before reading fresh data).</summary>
    public Task FlushAsync() => FlushToDatabase();

    public async Task OnApplicationExitAsync()
    {
        _saveTimer.Stop();
        _cursorSampleTimer.Stop();
        await FlushToDatabase();
    }

    public void Dispose()
    {
        _saveTimer.Stop();
        _saveTimer.Dispose();
        _cursorSampleTimer.Stop();
        _cursorSampleTimer.Dispose();
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
