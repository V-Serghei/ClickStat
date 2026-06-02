using System;
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
    public event Action<int>?    MouseButtonPressed; // button code (MouseButtons int value or keyboard-encoded)
    public event Action<int>?    MouseScrolled;      // notches

    public void PublishKey(string keyName)
    {
        if (KeyPressed == null) return;
        _ui.InvokeAsync(() => KeyPressed?.Invoke(keyName), DispatcherPriority.Background);
    }

    public void PublishMouseButton(int buttonCode)
    {
        if (MouseButtonPressed == null) return;
        _ui.InvokeAsync(() => MouseButtonPressed?.Invoke(buttonCode), DispatcherPriority.Background);
    }

    public void PublishMouseScroll(int notches)
    {
        if (MouseScrolled == null) return;
        _ui.InvokeAsync(() => MouseScrolled?.Invoke(notches), DispatcherPriority.Background);
    }
}
