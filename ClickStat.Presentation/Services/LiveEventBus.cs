using System;
using System.Diagnostics;
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
        Dispatch(() => MouseButtonPressed?.Invoke(buttonCode), DispatcherPriority.Input);
    }

    public void PublishMouseScroll(int notches)
    {
        if (MouseScrolled == null) return;
        Dispatch(() => MouseScrolled?.Invoke(notches), DispatcherPriority.Input);
    }

    private void Dispatch(Action action, DispatcherPriority priority = DispatcherPriority.Background)
    {
        ClickStat.Infrastructure.Diagnostics.PerformanceLog.CountUiDispatch();
        if (_ui.CheckAccess())
            action();
        else if (priority == DispatcherPriority.Input)
        {
            // Live mouse feedback must not wait until the input queue becomes idle.
            var queuedAt = Stopwatch.GetTimestamp();
            _ui.BeginInvoke((Action)(() =>
            {
                var delay = Stopwatch.GetElapsedTime(queuedAt);
                if (delay.TotalMilliseconds >= 100)
                    ClickStat.Infrastructure.Diagnostics.PerformanceLog.Record(
                        "slow", "mouse.ui_dispatch", (long)delay.TotalMilliseconds);
                action();
            }), priority);
        }
        else
            _ui.BeginInvoke(action, priority);
    }
}
