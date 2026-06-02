using System;
using System.Windows.Forms;

namespace ClickStat.Infrastructure.InputMonitoring;

public class MouseMonitor : IDisposable
{
    private readonly RawMouseMonitor _raw = new();

    // button enum + int code (matching MouseButtons enum values)
    public event Action<MouseButtons, int>? ButtonPressed;
    // positive = scroll up notches, negative = scroll down notches
    public event Action<int>? Scrolled;

    private static readonly (MouseButtons btn, int code)[] ButtonMap =
    {
        (MouseButtons.Left,     (int)MouseButtons.Left),
        (MouseButtons.Right,    (int)MouseButtons.Right),
        (MouseButtons.Middle,   (int)MouseButtons.Middle),
        (MouseButtons.XButton1, (int)MouseButtons.XButton1),
        (MouseButtons.XButton2, (int)MouseButtons.XButton2),
    };

    public MouseMonitor()
    {
        _raw.ButtonDown += OnRawButton;
        _raw.Wheel      += notches => Scrolled?.Invoke(notches);
    }

    private void OnRawButton(int rawNumber)
    {
        // rawNumber: 1=Left, 2=Right, 3=Middle, 4=Back, 5=Forward
        int idx = rawNumber - 1;
        if (idx < 0 || idx >= ButtonMap.Length) return;
        var (btn, code) = ButtonMap[idx];
        ButtonPressed?.Invoke(btn, code);
    }

    /// <summary>
    /// Must be called after the main window is shown so the HWND exists.
    /// </summary>
    public void InitializeRawInput(IntPtr hwnd) => _raw.Initialize(hwnd);

    public void Subscribe()   { /* Raw Input is always active once Initialize is called */ }
    public void Unsubscribe() { /* kept for interface compatibility */ }

    public void Dispose() => _raw.Dispose();
}
