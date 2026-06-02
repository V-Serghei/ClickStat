using System.ComponentModel;
using System.Runtime.CompilerServices;
using ClickStat.Core.Interfaces;
using ClickStat.Core.Services;

namespace ClickStat.Presentation.ViewModels;

public class SettingsViewModel : INotifyPropertyChanged
{
    private readonly IStartupService _startupService;

    // Injected by MainViewModel after construction
    internal BreakReminderService? BreakReminder { get; set; }

    public bool IsInStartup
    {
        get => _startupService.IsInStartup();
        set
        {
            if (value) _startupService.AddToStartup();
            else       _startupService.RemoveFromStartup();
            OnPropertyChanged();
        }
    }

    // ── Break reminder ─────────────────────────────────────────────────────

    private bool _breakEnabled;
    public bool BreakEnabled
    {
        get => _breakEnabled;
        set
        {
            _breakEnabled = value;
            if (BreakReminder != null) BreakReminder.IsEnabled = value;
            OnPropertyChanged();
        }
    }

    private int _breakInterval = 45;
    public int BreakInterval
    {
        get => _breakInterval;
        set
        {
            _breakInterval = System.Math.Clamp(value, 5, 240);
            if (BreakReminder != null) BreakReminder.IntervalMinutes = _breakInterval;
            OnPropertyChanged();
        }
    }

    public SettingsViewModel(IStartupService startupService)
    {
        _startupService = startupService;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
