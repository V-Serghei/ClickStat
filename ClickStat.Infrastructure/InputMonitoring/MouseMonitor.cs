using System;
using System.Windows.Forms;
using Gma.System.MouseKeyHook;

namespace ClickStat.Infrastructure.InputMonitoring;

public class MouseMonitor : IDisposable
{
    private IKeyboardMouseEvents? _globalHook;

    // button enum value (MouseButtons) + raw int code
    public event Action<MouseButtons, int>? ButtonPressed;
    // positive = scroll up notches, negative = scroll down notches
    public event Action<int>? Scrolled;

    public void Subscribe()
    {
        _globalHook = Hook.GlobalEvents();
        _globalHook.MouseDown += OnMouseDown;
        _globalHook.MouseWheel += OnMouseWheel;
    }

    public void Unsubscribe()
    {
        if (_globalHook == null) return;
        _globalHook.MouseDown -= OnMouseDown;
        _globalHook.MouseWheel -= OnMouseWheel;
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
        if (notches != 0)
            Scrolled?.Invoke(notches);
    }

    public void Dispose() => Unsubscribe();
}
