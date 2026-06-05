using System;
using System.Windows.Forms;
using Gma.System.MouseKeyHook;

namespace ClickStat.Infrastructure.InputMonitoring;

/// <summary>
/// Primary mouse monitor — uses WH_MOUSE_LL global hook (Gma.System.MouseKeyHook).
/// Works in background regardless of window focus or visibility.
/// RawMouseMonitor is kept as a supplemental hook for extra-button detection.
/// </summary>
public class MouseMonitor : IDisposable
{
    private IKeyboardMouseEvents? _globalHook;
    private readonly RawMouseMonitor _raw = new();

    // Position tracking for distance calculation (screen pixels)
    private int  _lastX = -1;
    private int  _lastY = -1;

    public event Action<MouseButtons, int>? ButtonPressed; // button, button code
    public event Action<int>?              Scrolled;       // notches (positive=up)
    public event Action<int, int>?         Moved;          // screen-pixel delta (dx, dy)

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
        _globalHook = Hook.GlobalEvents();
        _globalHook.MouseDown  += OnMouseDown;
        _globalHook.MouseWheel += OnMouseWheel;
        _globalHook.MouseMove  += OnMouseMove;
    }

    public void Unsubscribe()
    {
        if (_globalHook == null) return;
        _globalHook.MouseDown  -= OnMouseDown;
        _globalHook.MouseWheel -= OnMouseWheel;
        _globalHook.MouseMove  -= OnMouseMove;
        _globalHook.Dispose();
        _globalHook = null;
    }

    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
        ButtonPressed?.Invoke(e.Button, (int)e.Button);
    }

    private void OnMouseWheel(object? sender, MouseEventArgs e)
    {
        int notches = e.Delta / 120;
        if (notches != 0) Scrolled?.Invoke(notches);
    }

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        if (_lastX >= 0)
        {
            int dx = e.X - _lastX;
            int dy = e.Y - _lastY;
            if (dx != 0 || dy != 0)
                Moved?.Invoke(dx, dy);
        }
        _lastX = e.X;
        _lastY = e.Y;
    }

    /// <summary>
    /// Optional: register Raw Input hook for buttons beyond XButton2 (G502X extra buttons).
    /// Must be called after main window is shown.
    /// </summary>
    public void InitializeRawInput(IntPtr hwnd) => _raw.Initialize(hwnd);

    public void Dispose()
    {
        Unsubscribe();
        _raw.Dispose();
    }
}
