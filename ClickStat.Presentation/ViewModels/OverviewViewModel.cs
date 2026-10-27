using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
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
    public enum HistoryScale { Days, Months, Years }
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
    private HistoryScale _historyScale = HistoryScale.Days;
    private DateTime _historyAnchor = DateTime.Today;
    private int _chartLoadVersion;
    private string _historyPeriod = "";
    private int _historyTotal;

    public ICommand SelectHistoryScaleCommand { get; }
    public ICommand ShiftHistoryCommand { get; }
    public string HistoryPeriod { get => _historyPeriod; private set { _historyPeriod = value; OnPropertyChanged(); } }
    public int HistoryTotal { get => _historyTotal; private set { _historyTotal = value; OnPropertyChanged(); } }
    public bool CanGoForward => _historyAnchor.Date < DateTime.Today;
    public bool IsDaysSelected => _historyScale == HistoryScale.Days;
    public bool IsMonthsSelected => _historyScale == HistoryScale.Months;
    public bool IsYearsSelected => _historyScale == HistoryScale.Years;

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
        SelectHistoryScaleCommand = new RelayCommand(p =>
        {
            if (p is not string value || !Enum.TryParse<HistoryScale>(value, out var scale)) return;
            _historyScale = scale;
            _historyAnchor = DateTime.Today;
            NotifyHistorySelection();
            _ = LoadChartAsync();
        });
        ShiftHistoryCommand = new RelayCommand(p =>
        {
            if (p is not string value || !int.TryParse(value, out var direction)) return;
            var next = _historyScale switch
            {
                HistoryScale.Days => _historyAnchor.AddDays(10 * direction),
                HistoryScale.Months => _historyAnchor.AddYears(direction),
                _ => _historyAnchor.AddYears(10 * direction)
            };
            if (direction > 0 && next > DateTime.Today) next = DateTime.Today;
            if (next == _historyAnchor) return;
            _historyAnchor = next;
            OnPropertyChanged(nameof(CanGoForward));
            _ = LoadChartAsync();
        });
        InitChartStyle();

        // Always accumulate session keys — even when Overview tab is closed
        _liveBus.KeyPressed += OnLiveKey;
        LocalizationService.Instance.PropertyChanged += OnLocalizationChanged;
    }

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "Item[]" || e.PropertyName == nameof(LocalizationService.CurrentLanguage))
        {
            RefreshLocalizedChartText();
            _ = LoadChartAsync();
        }
    }

    // ── DB load (called on tab open) ──────────────────────────────────────
    public async Task LoadStatsAsync()
    {
        using var timing = ClickStat.Infrastructure.Diagnostics.PerformanceLog.Measure("overview.load");
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
        var version = ++_chartLoadVersion;
        var culture = CultureInfo.GetCultureInfo(LocalizationService.Instance.CurrentLanguage);
        var text      = new SKColor(200, 200, 200);
        var separator = new SKColor(100, 100, 100);
        var scale = _historyScale;
        var anchor = _historyAnchor;
        DateTime start;
        DateTime end;
        DateTime[] points;
        if (scale == HistoryScale.Days)
        {
            end = anchor.Date;
            start = end.AddDays(-9);
            points = Enumerable.Range(0, 10).Select(i => start.AddDays(i)).ToArray();
        }
        else if (scale == HistoryScale.Months)
        {
            start = new DateTime(anchor.Year, 1, 1);
            end = start.AddYears(1).AddDays(-1);
            points = Enumerable.Range(0, 12).Select(i => start.AddMonths(i)).ToArray();
        }
        else
        {
            start = new DateTime(Math.Max(1, anchor.Year - 9), 1, 1);
            end = start.AddYears(10).AddDays(-1);
            points = Enumerable.Range(0, 10).Select(i => start.AddYears(i)).ToArray();
        }

        var lookup = await _dataService.GetDailyClickCounts(start, end);
        if (version != _chartLoadVersion) return;
        if (DateTime.Today >= start && DateTime.Today <= end)
        {
            lookup.TryGetValue(DateTime.Today, out var today);
            lookup[DateTime.Today] = Math.Max(today, ClicksToday);
        }
        var counts = points.Select(point => lookup.Where(pair => scale switch
        {
            HistoryScale.Days => pair.Key.Date == point.Date,
            HistoryScale.Months => pair.Key.Year == point.Year && pair.Key.Month == point.Month,
            _ => pair.Key.Year == point.Year
        }).Sum(pair => pair.Value)).ToArray();
        HistoryTotal = counts.Sum();
        HistoryPeriod = scale switch
        {
            HistoryScale.Days => $"{start.ToString("d", culture)} — {end.ToString("d", culture)}",
            HistoryScale.Months => anchor.Year.ToString(CultureInfo.CurrentCulture),
            _ => $"{start.Year}–{end.Year}"
        };

        DailyStatsSeries = new ISeries[]
        {
            new ColumnSeries<int>
            {
                Name   = LocalizationService.Instance["Overview.Presses"],
                Values = counts,
                Fill   = new SolidColorPaint(new SKColor(170, 112, 255))
            }
        };
        XAxes = new Axis[]
        {
            new Axis
            {
                Labels          = points.Select(d => scale switch
                {
                    HistoryScale.Days => d.ToString("dd MMM", culture),
                    HistoryScale.Months => d.ToString("MMM", culture),
                    _ => d.ToString("yyyy", culture)
                }).ToArray(),
                LabelsRotation  = 15,
                TextSize        = 12,
                NamePaint       = new SolidColorPaint(text),
                LabelsPaint     = new SolidColorPaint(text),
                SeparatorsPaint = new SolidColorPaint(separator)
            }
        };
    }

    private void NotifyHistorySelection()
    {
        OnPropertyChanged(nameof(IsDaysSelected));
        OnPropertyChanged(nameof(IsMonthsSelected));
        OnPropertyChanged(nameof(IsYearsSelected));
        OnPropertyChanged(nameof(CanGoForward));
    }

    // ── Live event handler (UI thread via LiveEventBus) ───────────────────
    private void RefreshLocalizedChartText()
    {
        if (DailyStatsSeries is null)
            return;

        foreach (var series in DailyStatsSeries)
            series.Name = LocalizationService.Instance["Overview.Presses"];

        OnPropertyChanged(nameof(DailyStatsSeries));
    }

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
