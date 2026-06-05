using System;
using System.Windows.Forms;
using ClickStat.Core.Interfaces;
using ClickStat.Infrastructure.InputMonitoring;

namespace ClickStat.Core.Services;

public class MouseMonitorService : IMouseMonitorService
{
    private readonly MouseMonitor _monitor = new();

    public event Action<MouseButtons, int>? OnButtonPressed;
    public event Action<int>? OnScroll;
    public event Action<int, int>? OnMoved;

    public MouseMonitorService()
    {
        _monitor.ButtonPressed += (btn, code) => OnButtonPressed?.Invoke(btn, code);
        _monitor.Scrolled      += notches => OnScroll?.Invoke(notches);
        _monitor.Moved         += (dx, dy) => OnMoved?.Invoke(dx, dy);
    }

    public void StartMonitoring() => _monitor.Subscribe();
    public void StopMonitoring()  => _monitor.Unsubscribe();
    public void InitializeRawInput(IntPtr hwnd) => _monitor.InitializeRawInput(hwnd);
}
