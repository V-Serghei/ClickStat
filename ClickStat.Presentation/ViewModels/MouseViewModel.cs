using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Threading;
using ClickStat.Core.Interfaces;
using ClickStat.Presentation.Model;
using ClickStat.Presentation.Services;
using ClickStat.Presentation.Views;

namespace ClickStat.Presentation.ViewModels;

public class MouseViewModel : INotifyPropertyChanged
{
    private readonly IMouseStatisticsService _statisticsService;
    private readonly LiveEventBus            _liveBus;

    // ── Standard buttons ───────────────────────────────────────────────────
    public MouseButtonStat LeftButton    { get; } = new((int)MouseButtons.Left,     "Левая кнопка");
    public MouseButtonStat RightButton   { get; } = new((int)MouseButtons.Right,    "Правая кнопка");
    public MouseButtonStat MiddleButton  { get; } = new((int)MouseButtons.Middle,   "Колесо (клик)");
    public MouseButtonStat BackButton    { get; } = new((int)MouseButtons.XButton1, "Кнопка назад");
    public MouseButtonStat ForwardButton { get; } = new((int)MouseButtons.XButton2, "Кнопка вперёд");

    // ── Custom buttons ────────────────────────────────────────────────────
    public ObservableCollection<MouseButtonStat> CustomButtons { get; } = new();

    // ── Session deltas (accumulated regardless of active tab) ──────────────
    // Reset to zero each time LoadDataAsync() is called (fresh DB load).
    // Display = DB_value + session_delta.
    private readonly Dictionary<int, long> _sessionClicks = new();
    private long _sessionScrollUp;
    private long _sessionScrollDown;

    // ── DB baseline values ─────────────────────────────────────────────────
    private long _dbScrollUp;
    private long _dbScrollDown;

    // ── Displayed scroll values ────────────────────────────────────────────
    private long _scrollUpNotches;
    private long _scrollDownNotches;

    public long ScrollUpNotches
    {
        get => _scrollUpNotches;
        private set { _scrollUpNotches = value; OnPropertyChanged(); OnPropertyChanged(nameof(ScrollUpRotations)); OnPropertyChanged(nameof(ScrollUpMeters)); }
    }
    public long ScrollDownNotches
    {
        get => _scrollDownNotches;
        private set { _scrollDownNotches = value; OnPropertyChanged(); OnPropertyChanged(nameof(ScrollDownRotations)); OnPropertyChanged(nameof(ScrollDownMeters)); }
    }

    public double ScrollUpRotations   => Math.Round(_scrollUpNotches   / 24.0, 1);
    public double ScrollDownRotations => Math.Round(_scrollDownNotches / 24.0, 1);
    public double ScrollUpMeters      => Math.Round(_scrollUpNotches   * 0.0021, 2);
    public double ScrollDownMeters    => Math.Round(_scrollDownNotches * 0.0021, 2);

    // ── Loading ────────────────────────────────────────────────────────────
    private bool _isLoading;
    public bool IsLoading { get => _isLoading; private set { _isLoading = value; OnPropertyChanged(); } }

    // ── Live mode: controls animations only, NOT data accumulation ──────────
    private bool _isLiveActive;
    public bool IsLiveActive
    {
        get => _isLiveActive;
        set
        {
            if (_isLiveActive == value) return;
            _isLiveActive = value;
            OnPropertyChanged();
            if (!_isLiveActive)
            {
                // Cancel any running flash animations
                foreach (var b in AllButtons) b.IsActive = false;
                _flashExpiry.Clear();
                _flashTimer.Stop();
            }
        }
    }

    // ── Flash timer (animations) ────────────────────────────────────────────
    private readonly DispatcherTimer _flashTimer;
    private readonly Dictionary<int, DateTime> _flashExpiry = new();

    // ── Commands ───────────────────────────────────────────────────────────
    public ICommand AddButtonCommand { get; }
    public ICommand RefreshCommand   { get; }

    // ── Constructor ────────────────────────────────────────────────────────
    public MouseViewModel(IMouseStatisticsService statisticsService, LiveEventBus liveBus)
    {
        _statisticsService = statisticsService;
        _liveBus           = liveBus;

        _flashTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _flashTimer.Tick += OnFlashTick;

        AddButtonCommand = new RelayCommand(_ => OpenAddButtonDialog());
        RefreshCommand   = new RelayCommand(async _ => await LoadDataAsync());

        // Always subscribe to live bus — accumulate session deltas even when tab is closed
        _liveBus.MouseButtonPressed += OnButtonPressedAlways;
        _liveBus.MouseScrolled      += OnScrolledAlways;

        _ = LoadDataAsync();
    }

    // ── DB load ─────────────────────────────────────────────────────────────
    public async Task LoadDataAsync()
    {
        IsLoading = true;
        try
        {
            var buttons = await _statisticsService.GetButtonStatistics();
            var scroll  = await _statisticsService.GetScrollStatistics();

            // Reset session deltas — we now have fresh DB data
            lock (_sessionClicks) { _sessionClicks.Clear(); }
            _sessionScrollUp   = 0;
            _sessionScrollDown = 0;

            var standardCodes = new HashSet<int>
            {
                (int)MouseButtons.Left, (int)MouseButtons.Right, (int)MouseButtons.Middle,
                (int)MouseButtons.XButton1, (int)MouseButtons.XButton2
            };

            // Set DB baseline for standard buttons
            foreach (var btn in buttons)
            {
                var stat = FindButton(btn.ButtonCode);
                if (stat != null) stat.Count = btn.Count;
            }

            CustomButtons.Clear();
            foreach (var btn in buttons.Where(b => !standardCodes.Contains(b.ButtonCode) && b.IsRegistered))
                CustomButtons.Add(new MouseButtonStat(btn.ButtonCode, btn.ButtonName) { Count = btn.Count });

            // DB baseline for scroll
            _dbScrollUp   = scroll?.ScrollUpNotches   ?? 0;
            _dbScrollDown = scroll?.ScrollDownNotches ?? 0;
            ScrollUpNotches   = _dbScrollUp;
            ScrollDownNotches = _dbScrollDown;
        }
        finally { IsLoading = false; }
    }

    // ── Always-on live handlers (accumulate session regardless of active tab) ──

    private void OnButtonPressedAlways(int buttonCode)
    {
        // Accumulate session delta
        lock (_sessionClicks)
            _sessionClicks[buttonCode] = _sessionClicks.GetValueOrDefault(buttonCode) + 1;

        // Update displayed count
        var stat = FindButton(buttonCode);
        if (stat != null) stat.Count++;

        // Flash animation only when this tab is open
        if (_isLiveActive)
        {
            stat?.DoActive();
            _flashExpiry[buttonCode] = DateTime.Now.AddMilliseconds(180);
            if (!_flashTimer.IsEnabled) _flashTimer.Start();
        }
    }

    private void OnScrolledAlways(int notches)
    {
        if (notches > 0)
        {
            _sessionScrollUp  += notches;
            ScrollUpNotches    = _dbScrollUp + _sessionScrollUp;
        }
        else
        {
            var abs = Math.Abs(notches);
            _sessionScrollDown += abs;
            ScrollDownNotches   = _dbScrollDown + _sessionScrollDown;
        }
    }

    private void OnFlashTick(object? sender, EventArgs e)
    {
        var now     = DateTime.Now;
        var expired = _flashExpiry.Where(kv => kv.Value <= now).Select(kv => kv.Key).ToList();
        foreach (var code in expired)
        {
            _flashExpiry.Remove(code);
            var b = FindButton(code);
            if (b != null) b.IsActive = false;
        }
        if (_flashExpiry.Count == 0) _flashTimer.Stop();
    }

    // ── Helpers ────────────────────────────────────────────────────────────
    private IEnumerable<MouseButtonStat> AllButtons =>
        new[] { LeftButton, RightButton, MiddleButton, BackButton, ForwardButton }
            .Concat(CustomButtons);

    private MouseButtonStat? FindButton(int code)
    {
        if (code == (int)MouseButtons.Left)     return LeftButton;
        if (code == (int)MouseButtons.Right)    return RightButton;
        if (code == (int)MouseButtons.Middle)   return MiddleButton;
        if (code == (int)MouseButtons.XButton1) return BackButton;
        if (code == (int)MouseButtons.XButton2) return ForwardButton;
        return CustomButtons.FirstOrDefault(b => b.ButtonCode == code);
    }

    private async void OpenAddButtonDialog()
    {
        var dialog = new AddMouseButtonDialog { Owner = System.Windows.Application.Current.MainWindow };
        if (dialog.ShowDialog() == true && dialog.RegisteredButton.HasValue)
        {
            var (code, name) = dialog.RegisteredButton.Value;
            await _statisticsService.RegisterCustomButton(code, name);
            await LoadDataAsync();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
