using System;
using System.Windows;
using System.Windows.Threading;

namespace ClickStat.Core.Services;

/// <summary>
/// Fires a reminder notification when the user has been typing
/// continuously for longer than the configured interval.
/// </summary>
public sealed class BreakReminderService : IDisposable
{
    private readonly DispatcherTimer _idleTimer;
    private int     _intervalMinutes = 45;
    private bool    _enabled         = false;
    private DateTime _lastActivity   = DateTime.Now;

    public event Action<int>? ReminderTriggered; // fires with interval in minutes

    public BreakReminderService()
    {
        _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _idleTimer.Tick += OnTick;
    }

    public int  IntervalMinutes
    {
        get => _intervalMinutes;
        set { _intervalMinutes = Math.Max(5, value); Reset(); }
    }

    public bool IsEnabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            if (_enabled) { Reset(); _idleTimer.Start(); }
            else            _idleTimer.Stop();
        }
    }

    public void RecordActivity() => _lastActivity = DateTime.Now;

    private void OnTick(object? sender, EventArgs e)
    {
        if (!_enabled) return;
        var elapsed = (DateTime.Now - _lastActivity).TotalMinutes;
        if (elapsed >= _intervalMinutes)
        {
            ReminderTriggered?.Invoke(_intervalMinutes);
            Reset();
        }
    }

    private void Reset() => _lastActivity = DateTime.Now;

    public void Dispose() => _idleTimer.Stop();
}
