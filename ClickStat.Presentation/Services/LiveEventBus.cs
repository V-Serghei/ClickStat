using System;
using System.Windows.Forms;
using System.Windows.Threading;

namespace ClickStat.Presentation.Services;

/// <summary>
/// Thread-safe event bus for real-time input events.
/// Always fires subscribers on the UI thread so ViewModels need no extra marshaling.
/// Publishers call from any thread; the bus does the Dispatcher.InvokeAsync internally.
/// </summary>
public sealed class LiveEventBus
{
    private readonly Dispatcher _ui;

    public LiveEventBus(Dispatcher uiDispatcher) => _ui = uiDispatcher;

    public event Action<string>? KeyPressed;        // keyName (Keys.ToString())
    public event Action<Keys, bool>? KeyInput;       // physical key and shift state
    public event Action<int>?    MouseButtonPressed; // button code (MouseButtons int value or keyboard-encoded)
    public event Action<int>?    MouseScrolled;      // notches

    public void PublishKey(string keyName)
    {
        if (KeyPressed == null) return;
        Dispatch(() => KeyPressed?.Invoke(keyName));
    }

    public void PublishKeyInput(Keys key, bool shift)
    {
        if (KeyInput == null) return;
        Dispatch(() => KeyInput?.Invoke(key, shift));
    }

    public void PublishMouseButton(int buttonCode)
    {
        if (MouseButtonPressed == null) return;
        Dispatch(() => MouseButtonPressed?.Invoke(buttonCode));
    }

    public void PublishMouseScroll(int notches)
    {
        if (MouseScrolled == null) return;
        Dispatch(() => MouseScrolled?.Invoke(notches));
    }

    private void Dispatch(Action action)
    {
        ClickStat.Infrastructure.Diagnostics.PerformanceLog.CountUiDispatch();
        if (_ui.CheckAccess())
            action();
        else
            _ui.BeginInvoke(action, DispatcherPriority.Background);
    }
}
