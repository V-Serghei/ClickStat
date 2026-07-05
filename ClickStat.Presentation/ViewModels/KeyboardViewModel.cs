using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Threading;
using ClickStat.Core.Interfaces;
using ClickStat.Infrastructure.Data;
using ClickStat.Infrastructure.Services;
using ClickStat.Presentation.Model;
using ClickStat.Presentation.Services;

namespace ClickStat.Presentation.ViewModels;

public class KeyboardViewModel : INotifyPropertyChanged
{
    private readonly IGetDataClick _dataClickService;
    private readonly LiveEventBus  _liveBus;
    private readonly InputTemplateProcessor _inputTemplateProcessor;
    private readonly ITypingTrainerWindowService _typingTrainerWindowService;

    // Loading state

    private bool _isLoading = true;
    public bool IsLoading
    {
        get => _isLoading;
        set { _isLoading = value; OnPropertyChanged(); }
    }

    // Key counts

    public Dictionary<string, int> KeyCounts { get; private set; } = new();

    private int _maxCount;
    public int MaxCount { get => _maxCount; set { _maxCount = value; OnPropertyChanged(); } }

    // Custom keys

    public ObservableCollection<CustomKeyItem> CustomKeys { get; private set; } = new();

    // Session input buffer. Kept only while the keyboard tab/window is open.
    private readonly StringBuilder _sessionInput = new();

    private string _sessionInputText = "";
    public string SessionInputText
    {
        get => _sessionInputText;
        private set
        {
            if (_sessionInputText == value) return;
            _sessionInputText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SessionInputLength));
        }
    }

    public int SessionInputLength => _sessionInput.Length;

    private string _sessionInputStatus = "";
    public string SessionInputStatus
    {
        get => _sessionInputStatus;
        private set { _sessionInputStatus = value; OnPropertyChanged(); }
    }

    public ICommand ClearSessionInputCommand { get; }
    public ICommand CopySessionInputCommand { get; }
    public ICommand SaveSessionInputCommand { get; }
    public ICommand OpenTypingTrainerCommand { get; }

    // Keyboard layout

    private string _layoutCode = "EN";
    private string _layoutName = "";

    public string LayoutCode
    {
        get => _layoutCode;
        private set { _layoutCode = value; OnPropertyChanged(); }
    }
    public string LayoutName
    {
        get => _layoutName;
        private set { _layoutName = value; OnPropertyChanged(); }
    }

    // Flash state

    public HashSet<string> FlashingKeys { get; } = new();
    private readonly Dictionary<string, DateTime> _flashExpiry = new();
    private readonly DispatcherTimer _flashTimer;
    private readonly DispatcherTimer _layoutTimer;

    // WPM tracking

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

    // Live mode

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
            {
                UpdateLayoutInfo(force: true);
                _layoutTimer.Start();
    // Constructor
            }
            else
            {
                _layoutTimer.Stop();
                // Clear flash animations only
                FlashingKeys.Clear();
                _flashExpiry.Clear();
                _flashTimer.Stop();
                OnPropertyChanged(nameof(FlashingKeys));
            }
        }
    }

    // Constructor

    public KeyboardViewModel(
        IGetDataClick dataClickService,
        LiveEventBus liveBus,
        InputTemplateProcessor inputTemplateProcessor,
        ITypingTrainerWindowService typingTrainerWindowService)
    {
        _dataClickService = dataClickService;
        _liveBus          = liveBus;
        _inputTemplateProcessor = inputTemplateProcessor;
        _typingTrainerWindowService = typingTrainerWindowService;

        ClearSessionInputCommand = new RelayCommand(_ => ClearSessionInput());
        CopySessionInputCommand = new RelayCommand(_ => CopySessionInput());
        SaveSessionInputCommand = new RelayCommand(async _ => await SaveSessionInputAsync());
        OpenTypingTrainerCommand = new RelayCommand(_ => OpenTypingTrainer());

        _flashTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _flashTimer.Tick += OnFlashTick;

        _layoutTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _layoutTimer.Tick += (_, _) => UpdateLayoutInfo();
        UpdateLayoutInfo(force: true);

        // Always subscribe so key presses accumulate regardless of the active tab.
        _liveBus.KeyPressed += OnLiveKeyPress;

    }

    private void OpenTypingTrainer()
    {
        _typingTrainerWindowService.Show();
    }

    // Database load

    public async Task LoadKeyCountsAsync()
    {
        IsLoading = true;
        await Task.Yield();

        var stats = await Task.Run(async () => await _dataClickService.GetKeyStatistics());
        var mergedCounts = await Task.Run(() =>
        {
            var counts = new Dictionary<string, int>();
            foreach (var stat in stats)
            {
                foreach (var displayKey in GetDisplayKeys(stat.KeyName))
                    counts[displayKey] = counts.GetValueOrDefault(displayKey) + stat.Count;
            }

            return counts;
        });

        foreach (var (key, liveCount) in KeyCounts)
        {
            if (!mergedCounts.TryGetValue(key, out var dbCount) || liveCount > dbCount)
                mergedCounts[key] = liveCount;
        }

        KeyCounts  = mergedCounts;
        MaxCount   = KeyCounts.Values.Any() ? KeyCounts.Values.Max() : 1;

        var custom = mergedCounts
            .Where(kv => !KeyboardLayoutDefinition.StandardKeyNames.Contains(kv.Key) && !IsIgnoredSyntheticKey(kv.Key) && kv.Value > 0)
            .OrderByDescending(kv => kv.Value)
            .Select(kv => new CustomKeyItem { KeyName = kv.Key, Count = kv.Value });

        CustomKeys = new ObservableCollection<CustomKeyItem>(custom);

        UpdateLayoutInfo(force: true);
        OnPropertyChanged(string.Empty);
        IsLoading = false;
    }

    public void BeginLoading() => IsLoading = true;

    // Live key press handler

    private void OnLiveKeyPress(string keyName)
    {
        var displayKeys = GetDisplayKeys(keyName).ToList();

        // 1. Update in-memory count (no DB query needed)
        foreach (var displayKey in displayKeys)
        {
            if (KeyCounts.TryGetValue(displayKey, out var current))
                KeyCounts[displayKey] = current + 1;
            else
                KeyCounts[displayKey] = 1;

            if (KeyCounts[displayKey] > MaxCount)
                MaxCount = KeyCounts[displayKey];
        }

        OnPropertyChanged(nameof(KeyCounts)); // refresh heatmap colours

        // 2. Flash effect
        var flashUntil = DateTime.Now.AddMilliseconds(110);
        foreach (var displayKey in displayKeys)
        {
            _flashExpiry[displayKey] = flashUntil;
            FlashingKeys.Add(displayKey);
        }
        OnPropertyChanged(nameof(FlashingKeys));
        if (!_flashTimer.IsEnabled) _flashTimer.Start();

        // 2b. Update layout immediately on input; timer also catches layout hotkeys.
        UpdateLayoutInfo();

        // 3. WPM (skip pure modifiers because they do not count as typed characters)
        if (!IsModifierName(keyName))
        {
            var now = DateTime.Now;
            _wpmWindow.Enqueue(now);

            var cutoff = now.AddSeconds(-WpmWindowSeconds);
            while (_wpmWindow.Count > 0 && _wpmWindow.Peek() < cutoff)
                _wpmWindow.Dequeue();

            // WPM = (chars / 5) * (60 / window_seconds)
            var wpm = Math.Round(_wpmWindow.Count / 5.0 * (60.0 / WpmWindowSeconds), 1);
            CurrentWpm = wpm;
            if (wpm > PeakWpm) PeakWpm = wpm;
        }

        // Custom keys
        if (!KeyboardLayoutDefinition.StandardKeyNames.Contains(keyName) && !IsIgnoredSyntheticKey(keyName))
        {
            var existing = CustomKeys.FirstOrDefault(k => k.KeyName == keyName);
            if (existing != null)
                existing.Count = KeyCounts.GetValueOrDefault(keyName);
            else
                CustomKeys.Insert(0, new CustomKeyItem { KeyName = keyName, Count = 1 });
        }
    }

    public void RecordSessionKey(Keys key, bool shift)
    {
        if (!IsLiveActive)
            return;

        if (key == Keys.Back)
        {
            if (_sessionInput.Length > 0)
                _sessionInput.Remove(_sessionInput.Length - 1, 1);
            RefreshSessionInput();
            return;
        }

        if (key is Keys.Enter or Keys.Return)
        {
            _sessionInput.AppendLine();
            RefreshSessionInput();
            return;
        }

        if (key == Keys.Tab)
        {
            _sessionInput.Append('\t');
            RefreshSessionInput();
            return;
        }

        var ch = KeyTextResolver.ResolveChar(key, shift);
        if (ch.HasValue && !char.IsControl(ch.Value))
        {
            _sessionInput.Append(ch.Value);
            RefreshSessionInput();
        }
    }

    private void ClearSessionInput()
    {
        _sessionInput.Clear();
        SessionInputStatus = "";
        RefreshSessionInput();
    }

    private void CopySessionInput()
    {
        if (_sessionInput.Length == 0)
            return;

        System.Windows.Clipboard.SetText(SessionInputText);
        SessionInputStatus = LocalizationService.Instance["Common.Copied"];
    }

    private async Task SaveSessionInputAsync()
    {
        if (_sessionInput.Length == 0)
            return;

        await _inputTemplateProcessor.SaveAsync(SessionInputText);
        SessionInputStatus = LocalizationService.Instance["Common.Saved"];
    }

    private void RefreshSessionInput()
    {
        SessionInputText = _sessionInput.ToString();
        if (_sessionInput.Length == 0)
            SessionInputStatus = "";
    }

    // Flash timer

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

    // Helpers

    private static bool IsModifierName(string name) => name is
        "LShiftKey" or "RShiftKey" or "ShiftKey"    or
        "LControlKey" or "RControlKey" or "ControlKey" or
        "LMenu" or "RMenu" or "Menu"                 or
        "LWin" or "RWin";

    private static char? ResolveChar(Keys key, bool shift)
    {
        var keyState = new byte[256];
        var keyCode = key & Keys.KeyCode;
        int virtualKey = (int)keyCode;
        if (virtualKey is <= 0 or > 255) return null;

        keyState[virtualKey] = 0x80;
        if ((GetKeyState((int)Keys.Capital) & 0x0001) != 0)
            keyState[(int)Keys.Capital] = 0x01;

        if (shift)
        {
            keyState[(int)Keys.ShiftKey] = 0x80;
            keyState[(int)Keys.LShiftKey] = 0x80;
        }

        var sb = new StringBuilder(4);
        var layout = LayoutService.GetCurrentKeyboardLayoutHandle();
        uint scanCode = (uint)MapVirtualKeyEx((uint)virtualKey, 0, layout);
        int result = ToUnicodeEx((uint)virtualKey, scanCode, keyState, sb, 4, 4, layout);
        return result > 0 && sb.Length > 0 ? sb[0] : null;
    }

    [DllImport("user32.dll")]
    private static extern int ToUnicodeEx(
        uint wVirtKey,
        uint wScanCode,
        byte[] lpKeyState,
        [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pwszBuff,
        int cchBuff,
        uint wFlags,
        IntPtr dwhkl);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKeyEx(uint uCode, uint uMapType, IntPtr dwhkl);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    private static IEnumerable<string> GetDisplayKeys(string keyName)
    {
        if (IsIgnoredSyntheticKey(keyName))
            yield break;

        yield return keyName;
    }

    private static bool IsIgnoredSyntheticKey(string keyName) =>
        keyName is "None" or "LButton, OemClear" or "LButton,OemClear" or "OemClear"
            or "ShiftKey" or "ControlKey" or "Menu";

    private void UpdateLayoutInfo(bool force = false)
    {
        var (code, name) = LayoutService.GetCurrent();
        if (!force && code == LayoutCode && name == LayoutName) return;

        LayoutCode = code;
        LayoutName = name;
        Converters.KeyNameToLabelConverter.CurrentLayoutCode = code;
    }

    // Keyboard layout
    // Property change

    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
