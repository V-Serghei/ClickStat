using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using ClickStat.Core.Interfaces;
using ClickStat.Presentation.Converters;
using ClickStat.Presentation.Services;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace ClickStat.Presentation.ViewModels;

public class OverviewViewModel : INotifyPropertyChanged
{
    private readonly IGetDataClick _dataService;
    private readonly LiveEventBus  _liveBus;

    private long _visibleBackspace;
    private DateTime _visibleTodayDate = DateTime.Today;

    // ── Published properties ──────────────────────────────────────────────
    private int    _totalClicks;
    private int    _clicksToday;
    private string _mostFrequentKey = "—";
    private double _errorRate;
    private bool   _isLoading;
    private bool   _isLiveActive;

    public int    TotalClicks     { get => _totalClicks;     set { _totalClicks = value;     OnPropertyChanged(); } }
    public int    ClicksToday     { get => _clicksToday;     set { _clicksToday = value;     OnPropertyChanged(); } }
    public string MostFrequentKey { get => _mostFrequentKey; set { _mostFrequentKey = value; OnPropertyChanged(); } }
    public double ErrorRate       { get => _errorRate;       set { _errorRate = value;       OnPropertyChanged(); } }
    public bool   IsLoading       { get => _isLoading;       set { _isLoading = value;       OnPropertyChanged(); } }

    public bool IsLiveActive
    {
        get => _isLiveActive;
        set
        {
            if (_isLiveActive == value) return;
            _isLiveActive = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LiveIndicatorVisible));
            // Note: bus subscription is permanent (in constructor).
            // IsLiveActive only controls LIVE indicator visibility.
        }
    }

    public bool LiveIndicatorVisible => _isLiveActive;

    // ── Chart ─────────────────────────────────────────────────────────────
    private ISeries[]? _series;
    private Axis[]?    _xAxes;
    private Axis[]?    _yAxes;

    public ISeries[]? DailyStatsSeries    { get => _series;  set { _series = value;  OnPropertyChanged(); } }
    public Axis[]?    XAxes               { get => _xAxes;   set { _xAxes  = value;  OnPropertyChanged(); } }
    public Axis[]?    YAxes               { get => _yAxes;   set { _yAxes  = value;  OnPropertyChanged(); } }
    public SolidColorPaint? LegendTextPaint        { get; private set; }
    public SolidColorPaint? TooltipBackgroundPaint { get; private set; }
    public SolidColorPaint? TooltipTextPaint       { get; private set; }

    // ── Constructor ───────────────────────────────────────────────────────
    public OverviewViewModel(IGetDataClick dataService, LiveEventBus liveBus)
    {
        _dataService = dataService;
        _liveBus     = liveBus;
        InitChartStyle();

        // Always accumulate session keys — even when Overview tab is closed
        _liveBus.KeyPressed += OnLiveKey;
    }

    // ── DB load (called on tab open) ──────────────────────────────────────
    public async Task LoadStatsAsync()
    {
        IsLoading = true;
        try
        {
            var dbTotal = await _dataService.GetKeyStatisticsForTheAllTime();
            var today   = await _dataService.GetKeyStatisticsForTheDay(DateTime.Today);
            var dbToday = today.FirstOrDefault()?.ClickCount ?? 0;

            var allKeys   = await _dataService.GetKeyStatistics();
            var backspace = allKeys.FirstOrDefault(k => k.KeyName == "Back");
            _visibleBackspace = Math.Max(_visibleBackspace, backspace?.Count ?? 0);

            // Read TotalClicks AFTER all awaits — live events (OnLiveKey) may have
            // incremented it while the DB queries were running. Taking the max ensures
            // we never show a stale value, and we never lose live increments.
            TotalClicks = Math.Max(dbTotal, TotalClicks);
            ClicksToday = Math.Max(dbToday, ClicksToday);
            _visibleTodayDate = DateTime.Today;

            MostFrequentKey = allKeys.Any()
                ? FriendlyKey(allKeys.OrderByDescending(k => k.Count).First().KeyName)
                : "N/A";

            RefreshErrorRate();

            // Chart
            await LoadChartAsync();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadChartAsync()
    {
        var text      = new SKColor(200, 200, 200);
        var separator = new SKColor(100, 100, 100);

        var dates  = Enumerable.Range(0, 10).Select(i => DateTime.Today.AddDays(-9 + i)).ToList();
        var lookup = await _dataService.GetDailyClickCounts(dates[0], dates[^1]);
        var counts = dates.Select(d => lookup.TryGetValue(d.Date, out var c) ? c : 0).ToList();
        if (dates[^1].Date == DateTime.Today)
            counts[^1] = Math.Max(counts[^1], ClicksToday);

        DailyStatsSeries = new ISeries[]
        {
            new ColumnSeries<int>
            {
                Name   = "Нажатия",
                Values = counts,
                Fill   = new SolidColorPaint(new SKColor(170, 112, 255))
            }
        };
        XAxes = new Axis[]
        {
            new Axis
            {
                Labels          = dates.Select(d => d.ToString("dd MMM")).ToArray(),
                LabelsRotation  = 15,
                TextSize        = 12,
                NamePaint       = new SolidColorPaint(text),
                LabelsPaint     = new SolidColorPaint(text),
                SeparatorsPaint = new SolidColorPaint(separator)
            }
        };
    }

    // ── Live event handler (UI thread via LiveEventBus) ───────────────────
    private void OnLiveKey(string keyName)
    {
        if (_visibleTodayDate != DateTime.Today)
        {
            _visibleTodayDate = DateTime.Today;
            ClicksToday = 0;
        }

        TotalClicks++;
        ClicksToday++;
        if (keyName == "Back")
            _visibleBackspace++;

        RefreshErrorRate();
    }

    private void RefreshErrorRate() => ErrorRate = TotalClicks > 0
        ? Math.Round((double)_visibleBackspace / TotalClicks * 100, 1)
        : 0;


    private void InitChartStyle()
    {
        var text      = new SKColor(200, 200, 200);
        var separator = new SKColor(100, 100, 100);
        LegendTextPaint        = new SolidColorPaint(text);
        TooltipBackgroundPaint = new SolidColorPaint(new SKColor(40, 40, 40));
        TooltipTextPaint       = new SolidColorPaint(text);

        YAxes = new Axis[]
        {
            new Axis
            {
                TextSize        = 12,
                NamePaint       = new SolidColorPaint(text),
                LabelsPaint     = new SolidColorPaint(text),
                SeparatorsPaint = new SolidColorPaint(separator)
            }
        };
    }

    /// <summary>Converts raw key name (e.g. "LShiftKey") to the friendly label shown on the key.</summary>
    private static string FriendlyKey(string rawName)
    {
        var conv = new KeyNameToLabelConverter();
        return conv.Convert(rawName, typeof(string), null!, CultureInfo.InvariantCulture) as string ?? rawName;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
