using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;
using Gma.System.MouseKeyHook;
using ThreadingTimer = System.Threading.Timer;

namespace ClickStat.Infrastructure.InputMonitoring;

/// <summary>
/// Primary mouse monitor — uses WH_MOUSE_LL global hook (Gma.System.MouseKeyHook).
/// Works in background regardless of window focus or visibility.
/// Work beyond coordinate capture is handled outside the global hook.
/// </summary>
public class MouseMonitor : IDisposable
{
    private IKeyboardMouseEvents? _globalHook;

    // Position tracking for distance calculation (screen pixels)
    private int  _lastX = -1;
    private int  _lastY = -1;
    private long _pendingDistanceUnits;
    private long _latestPosition;
    private int _draining;
    private int _actionWorkerScheduled;
    private readonly ConcurrentQueue<(MouseButtons button, int notches)> _actions = new();
    private readonly ThreadingTimer _movementTimer;

    public event Action<MouseButtons, int>? ButtonPressed; // button, button code
    public event Action<int>?              Scrolled;       // notches (positive=up)
    public event Action<long, int, int>?   Moved;          // accumulated distance and latest screen position

    public MouseMonitor() => _movementTimer = new ThreadingTimer(_ => DrainMovement());

    private static readonly (MouseButtons btn, int code)[] ButtonMap =
    {
        (MouseButtons.Left,     (int)MouseButtons.Left),
        (MouseButtons.Right,    (int)MouseButtons.Right),
        (MouseButtons.Middle,   (int)MouseButtons.Middle),
        (MouseButtons.XButton1, (int)MouseButtons.XButton1),
        (MouseButtons.XButton2, (int)MouseButtons.XButton2),
    };

    public void Subscribe()
    {
        if (_globalHook != null) return;
        _globalHook = Hook.GlobalEvents();
        _globalHook.MouseDown  += OnMouseDown;
        _globalHook.MouseWheel += OnMouseWheel;
        _globalHook.MouseMove  += OnMouseMove;
        _movementTimer.Change(100, 100);
    }

    public void Unsubscribe()
    {
        if (_globalHook == null) return;
        _movementTimer.Change(Timeout.Infinite, Timeout.Infinite);
        _globalHook.MouseDown  -= OnMouseDown;
        _globalHook.MouseWheel -= OnMouseWheel;
        _globalHook.MouseMove  -= OnMouseMove;
        _globalHook.Dispose();
        _globalHook = null;
    }

    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
        QueueAction(e.Button, 0);
    }

    private void OnMouseWheel(object? sender, MouseEventArgs e)
    {
        int notches = e.Delta / 120;
        if (notches != 0) QueueAction(MouseButtons.None, notches);
    }

    private void QueueAction(MouseButtons button, int notches)
    {
        _actions.Enqueue((button, notches));
        ScheduleActionWorker();
    }

    private void ScheduleActionWorker()
    {
        if (Interlocked.CompareExchange(ref _actionWorkerScheduled, 1, 0) == 0)
            ThreadPool.QueueUserWorkItem(_ => DrainActions());
    }

    private void DrainActions()
    {
        try
        {
            while (_actions.TryDequeue(out var action))
            {
                try
                {
                    if (action.button == MouseButtons.None) Scrolled?.Invoke(action.notches);
                    else ButtonPressed?.Invoke(action.button, (int)action.button);
                }
                catch (Exception ex)
                {
                    ClickStat.Infrastructure.Diagnostics.PerformanceLog.Record("error", "mouse.action." + ex.GetType().Name);
                }
            }
        }
        finally
        {
            Volatile.Write(ref _actionWorkerScheduled, 0);
            if (!_actions.IsEmpty) ScheduleActionWorker();
        }
    }

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        if (_lastX >= 0)
        {
            int dx = e.X - _lastX;
            int dy = e.Y - _lastY;
            if (dx != 0 || dy != 0)
            {
                ClickStat.Infrastructure.Diagnostics.PerformanceLog.CountMouseMove();
                long units = (long)(Math.Sqrt((double)dx * dx + (double)dy * dy) * 26.46);
                Interlocked.Exchange(ref _latestPosition, ((long)(uint)e.X << 32) | (uint)e.Y);
                Interlocked.Add(ref _pendingDistanceUnits, units);
            }
        }
        _lastX = e.X;
        _lastY = e.Y;
    }

    private void DrainMovement()
    {
        if (Interlocked.Exchange(ref _draining, 1) != 0) return;
        try
        {
            long units = Interlocked.Exchange(ref _pendingDistanceUnits, 0);
            if (units == 0) return;
            long packed = Interlocked.Read(ref _latestPosition);
            var watch = Stopwatch.StartNew();
            Moved?.Invoke(units, (int)(packed >> 32), (int)packed);
            if (watch.ElapsedMilliseconds >= 100)
                ClickStat.Infrastructure.Diagnostics.PerformanceLog.Record("slow", "mouse.movement_batch", watch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            ClickStat.Infrastructure.Diagnostics.PerformanceLog.Record("error", "mouse.movement." + ex.GetType().Name);
        }
        finally { Volatile.Write(ref _draining, 0); }
    }

    public void Dispose()
    {
        Unsubscribe();
        _movementTimer.Dispose();
    }
}
