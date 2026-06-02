using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Threading;
using ClickStat.Core.Interfaces;
using ClickStat.Presentation.Model;
using ClickStat.Presentation.Services;

namespace ClickStat.Presentation.ViewModels;

public class KeyboardViewModel : INotifyPropertyChanged
{
    private readonly IGetDataClick _dataClickService;
    private readonly LiveEventBus  _liveBus;

    // ── Loading state ──────────────────────────────────────────────────────

    private bool _isLoading = true;
    public bool IsLoading
    {
        get => _isLoading;
        set { _isLoading = value; OnPropertyChanged(); }
    }

    // ── Key counts (live-updated in active mode) ───────────────────────────

    public Dictionary<string, int> KeyCounts { get; private set; } = new();

    private int _maxCount;
    public int MaxCount { get => _maxCount; set { _maxCount = value; OnPropertyChanged(); } }

    // ── Custom keys ────────────────────────────────────────────────────────

    public ObservableCollection<CustomKeyItem> CustomKeys { get; private set; } = new();

    // ── Flash state (real-time key press highlight) ─────────────────────────

    public HashSet<string> FlashingKeys { get; } = new();
    private readonly Dictionary<string, DateTime> _flashExpiry = new();
    private readonly DispatcherTimer _flashTimer;

    // ── WPM tracking ───────────────────────────────────────────────────────

    private readonly Queue<DateTime> _wpmWindow = new();
    private const int WpmWindowSeconds = 10;

    private double _currentWpm;
    public double CurrentWpm
    {
        get => _currentWpm;
        private set { _currentWpm = value; OnPropertyChanged(); }
    }

    private double _peakWpm;
    public double PeakWpm
    {
        get => _peakWpm;
        private set { _peakWpm = value; OnPropertyChanged(); }
    }

    // ── Live mode ──────────────────────────────────────────────────────────

    private bool _isLiveActive;
    public bool IsLiveActive
    {
        get => _isLiveActive;
        set
        {
            if (_isLiveActive == value) return;
            _isLiveActive = value;
            OnPropertyChanged();

            if (_isLiveActive)
                _liveBus.KeyPressed += OnLiveKeyPress;
            else
            {
                _liveBus.KeyPressed -= OnLiveKeyPress;
                // Clear flash state on deactivate
                FlashingKeys.Clear();
                _flashExpiry.Clear();
                _flashTimer.Stop();
                OnPropertyChanged(nameof(FlashingKeys));
            }
        }
    }

    // ── Constructor ────────────────────────────────────────────────────────

    public KeyboardViewModel(IGetDataClick dataClickService, LiveEventBus liveBus)
    {
        _dataClickService = dataClickService;
        _liveBus          = liveBus;

        _flashTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _flashTimer.Tick += OnFlashTick;

        _ = LoadKeyCountsAsync();
    }

    // ── DB load ────────────────────────────────────────────────────────────

    public async Task LoadKeyCountsAsync()
    {
        IsLoading = true;
        await Task.Delay(200);

        var stats  = await _dataClickService.GetKeyStatistics();
        KeyCounts  = stats.ToDictionary(s => s.KeyName, s => s.Count);
        MaxCount   = KeyCounts.Values.Any() ? KeyCounts.Values.Max() : 1;

        var custom = stats
            .Where(s => !StandardKeys.Contains(s.KeyName) && s.Count > 0)
            .OrderByDescending(s => s.Count)
            .Select(s => new CustomKeyItem { KeyName = s.KeyName, Count = s.Count });

        CustomKeys = new ObservableCollection<CustomKeyItem>(custom);

        OnPropertyChanged(string.Empty);
        IsLoading = false;
    }

    // ── Live key press handler (always on UI thread via LiveEventBus) ──────

    private void OnLiveKeyPress(string keyName)
    {
        // 1. Update in-memory count (no DB query needed)
        if (KeyCounts.TryGetValue(keyName, out var current))
            KeyCounts[keyName] = current + 1;
        else
            KeyCounts[keyName] = 1;

        if (KeyCounts[keyName] > MaxCount)
            MaxCount = KeyCounts[keyName];

        OnPropertyChanged(nameof(KeyCounts)); // refresh heatmap colours

        // 2. Flash effect
        _flashExpiry[keyName] = DateTime.Now.AddMilliseconds(110);
        FlashingKeys.Add(keyName);
        OnPropertyChanged(nameof(FlashingKeys));
        if (!_flashTimer.IsEnabled) _flashTimer.Start();

        // 3. WPM (skip pure modifiers — they don't count as typed characters)
        if (!IsModifierName(keyName))
        {
            var now = DateTime.Now;
            _wpmWindow.Enqueue(now);

            var cutoff = now.AddSeconds(-WpmWindowSeconds);
            while (_wpmWindow.Count > 0 && _wpmWindow.Peek() < cutoff)
                _wpmWindow.Dequeue();

            // WPM = (chars / 5) × (60 / window_seconds)
            double wpm = Math.Round(_wpmWindow.Count / 5.0 * (60.0 / WpmWindowSeconds), 1);
            CurrentWpm = wpm;
            if (wpm > PeakWpm) PeakWpm = wpm;
        }

        // 4. Update custom keys if this key isn't in the standard layout
        if (!StandardKeys.Contains(keyName))
        {
            var existing = CustomKeys.FirstOrDefault(k => k.KeyName == keyName);
            if (existing != null)
                existing.Count = KeyCounts[keyName];
            else
                CustomKeys.Insert(0, new CustomKeyItem { KeyName = keyName, Count = 1 });
        }
    }

    // ── Flash timer ────────────────────────────────────────────────────────

    private void OnFlashTick(object? sender, EventArgs e)
    {
        var now     = DateTime.Now;
        var expired = _flashExpiry.Where(kv => kv.Value <= now).Select(kv => kv.Key).ToList();
        if (expired.Count == 0) return;

        foreach (var key in expired)
        {
            _flashExpiry.Remove(key);
            FlashingKeys.Remove(key);
        }
        OnPropertyChanged(nameof(FlashingKeys));

        if (_flashExpiry.Count == 0) _flashTimer.Stop();
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static bool IsModifierName(string name) => name is
        "LShiftKey" or "RShiftKey" or "ShiftKey"    or
        "LControlKey" or "RControlKey" or "ControlKey" or
        "LMenu" or "RMenu" or "Menu"                 or
        "LWin" or "RWin";

    // All key names shown in the XAML keyboard layout
    private static readonly HashSet<string> StandardKeys = new()
    {
        "Escape",
        "F1","F2","F3","F4","F5","F6","F7","F8","F9","F10","F11","F12",
        "Oemtilde","D1","D2","D3","D4","D5","D6","D7","D8","D9","D0",
        "OemMinus","Oemplus","Back",
        "Tab","Q","W","E","R","T","Y","U","I","O","P","Oem4","Oem6","OemPipe",
        "Capital","A","S","D","F","G","H","J","K","L","OemSemicolon","OemQuotes","Enter",
        "LShiftKey","Z","X","C","V","B","N","M","Oemcomma","OemPeriod","Oem2","RShiftKey",
        "LControlKey","LWin","LMenu","Space","RMenu","RControlKey",
        "PrintScreen","LaunchApplication2","MediaStop",
        "Insert","Home","PageUp","Delete","End","Next",
        "Up","Left","Down","Right",
        "MediaPreviousTrack","MediaPlayPause","MediaNextTrack",
        "VolumeMute","VolumeDown","VolumeUp",
        "Clear","NumLock","Divide","Multiply","Subtract",
        "NumPad7","NumPad8","NumPad9",
        "NumPad4","NumPad5","NumPad6",
        "NumPad1","NumPad2","NumPad3",
        "NumPad0","Decimal","Add"
    };

    // ── INotifyPropertyChanged ─────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
