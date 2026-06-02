using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using ClickStat.Core.Interfaces;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace ClickStat.Presentation.ViewModels;

public class OverviewViewModel : INotifyPropertyChanged
{
    private readonly IGetDataClick _dataService;

    private int    _totalClicks;
    private int    _clicksToday;
    private string _mostFrequentKey = "—";
    private double _errorRate;
    private bool   _isLoading;

    public int    TotalClicks      { get => _totalClicks;      set { _totalClicks = value;      OnPropertyChanged(); } }
    public int    ClicksToday      { get => _clicksToday;      set { _clicksToday = value;      OnPropertyChanged(); } }
    public string MostFrequentKey  { get => _mostFrequentKey;  set { _mostFrequentKey = value;  OnPropertyChanged(); } }
    public bool   IsLoading        { get => _isLoading;        set { _isLoading = value;        OnPropertyChanged(); } }

    /// <summary>Backspace count / total key presses × 100 — error rate proxy.</summary>
    public double ErrorRate        { get => _errorRate;        set { _errorRate = value;        OnPropertyChanged(); } }

    public ISeries[]? DailyStatsSeries   { get; private set; }
    public Axis[]?    XAxes               { get; private set; }
    public Axis[]?    YAxes               { get; private set; }
    public SolidColorPaint? LegendTextPaint       { get; private set; }
    public SolidColorPaint? TooltipBackgroundPaint{ get; private set; }
    public SolidColorPaint? TooltipTextPaint      { get; private set; }

    public OverviewViewModel(IGetDataClick dataService)
    {
        _dataService = dataService;
        InitChart();
        _ = LoadStatsAsync();
    }

    private void InitChart()
    {
        var text      = new SKColor(200, 200, 200);
        var separator = new SKColor(100, 100, 100);

        DailyStatsSeries = new ISeries[]
        {
            new ColumnSeries<int>
            {
                Name   = "Нажатия",
                Values = Array.Empty<int>(),
                Fill   = new SolidColorPaint(new SKColor(170, 112, 255))
            }
        };

        XAxes = new Axis[]
        {
            new Axis
            {
                LabelsRotation = 15, TextSize = 12,
                NamePaint      = new SolidColorPaint(text),
                LabelsPaint    = new SolidColorPaint(text),
                SeparatorsPaint= new SolidColorPaint(separator)
            }
        };
        YAxes = new Axis[]
        {
            new Axis
            {
                TextSize       = 12,
                NamePaint      = new SolidColorPaint(text),
                LabelsPaint    = new SolidColorPaint(text),
                SeparatorsPaint= new SolidColorPaint(separator)
            }
        };
        LegendTextPaint        = new SolidColorPaint(text);
        TooltipBackgroundPaint = new SolidColorPaint(new SKColor(40, 40, 40));
        TooltipTextPaint       = new SolidColorPaint(text);
    }

    public async Task LoadStatsAsync()
    {
        IsLoading = true;

        TotalClicks = await _dataService.GetKeyStatisticsForTheAllTime();

        var today = await _dataService.GetKeyStatisticsForTheDay(DateTime.Today);
        ClicksToday = today.FirstOrDefault()?.ClickCount ?? 0;

        var allKeys = await _dataService.GetKeyStatistics();
        MostFrequentKey = allKeys.Any()
            ? allKeys.OrderByDescending(k => k.Count).First().KeyName
            : "N/A";

        // Error rate: BackSpace count / total
        var backspace = allKeys.FirstOrDefault(k => k.KeyName == "Back");
        ErrorRate = TotalClicks > 0 && backspace != null
            ? Math.Round((double)backspace.Count / TotalClicks * 100, 1)
            : 0;

        // Chart
        var dates  = Enumerable.Range(0, 10).Select(i => DateTime.Today.AddDays(-9 + i)).ToList();
        var lookup = await _dataService.GetDailyClickCounts(dates[0], dates[^1]);
        var counts = dates.Select(d => lookup.TryGetValue(d.Date, out var c) ? c : 0).ToList();

        DailyStatsSeries![0].Values = counts;
        XAxes![0].Labels = dates.Select(d => d.ToString("dd MMM")).ToArray();

        OnPropertyChanged(nameof(DailyStatsSeries));
        OnPropertyChanged(nameof(XAxes));

        IsLoading = false;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
