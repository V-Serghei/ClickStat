using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using ClickStat.Infrastructure.Data;
using ClickStat.Infrastructure.Data.Model;
using ClickStat.Presentation.Services;

namespace ClickStat.Presentation.ViewModels;

public class HourCellVm
{
    public int    DayOfWeek { get; init; }
    public int    Hour      { get; init; }
    public int    Count     { get; init; }
    public double Intensity { get; init; } // 0.0 – 1.0
    public string Tooltip   { get; init; } = "";
}

public class DayActivityVm
{
    public int    DayOfWeek        { get; init; }
    public string DayName          { get; init; } = "";
    public int    Count            { get; init; }
    public double Coefficient      { get; init; }
    public double SharePercent     { get; init; }
    public double Intensity        { get; init; }
    public string CoefficientLabel => $"×{Coefficient:0.00}";
    public string ShareLabel       => $"{SharePercent:0.0}%";
}

public class ActivityViewModel : INotifyPropertyChanged
{
    private readonly HourlyActivityProcessor _hourlyProcessor;
    private readonly MouseDataProcessor      _mouseProcessor;

    private bool _isLoading;
    public bool IsLoading { get => _isLoading; set { _isLoading = value; OnPropertyChanged(); } }

    // 7 rows × 24 cols
    public List<HourCellVm> HeatmapCells { get; private set; } = new();
    public List<DayActivityVm> DayStats { get; private set; } = new();

    private long   _distanceUnits;
    public double  DistanceKm  => Math.Round(_distanceUnits / 100_000_000.0, 2); // 0.01mm → km
    public double  DistanceM   => Math.Round(_distanceUnits / 100_000.0, 0);     // 0.01mm → m
    public string  DistanceLabel => DistanceKm >= 1
        ? $"{DistanceKm:0.00} {LocalizationService.Instance["Units.Km"]}"
        : $"{DistanceM:0} {LocalizationService.Instance["Units.M"]}";
    public int TotalActivityCount { get; private set; }
    public string MostActiveDayLabel { get; private set; } = LocalizationService.Instance["Common.NoData"];

    public ActivityViewModel(HourlyActivityProcessor hourlyProcessor, MouseDataProcessor mouseProcessor)
    {
        _hourlyProcessor = hourlyProcessor;
        _mouseProcessor  = mouseProcessor;
        LocalizationService.Instance.PropertyChanged += OnLocalizationChanged;
    }

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != "Item[]" && e.PropertyName != nameof(LocalizationService.CurrentLanguage))
            return;

        RelocalizeLoadedData();
        OnPropertyChanged(nameof(DistanceLabel));
        OnPropertyChanged(nameof(MostActiveDayLabel));
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        await Task.Yield();
        try
        {
            var rows = await _hourlyProcessor.GetAll();
            int max  = rows.Count > 0 ? rows.Max(r => r.Count) : 1;

            HeatmapCells = rows.Select(r => new HourCellVm
            {
                DayOfWeek = r.DayOfWeek,
                Hour      = r.Hour,
                Count     = r.Count,
                Intensity = max > 0 ? (double)r.Count / max : 0,
                Tooltip   = BuildTooltip(r.DayOfWeek, r.Hour, r.Count)
            }).ToList();

            TotalActivityCount = rows.Sum(r => r.Count);
            var dayTotals = rows
                .GroupBy(r => r.DayOfWeek)
                .ToDictionary(g => g.Key, g => g.Sum(r => r.Count));

            int maxDay = dayTotals.Count > 0 ? dayTotals.Values.DefaultIfEmpty(0).Max() : 0;
            double averageDay = TotalActivityCount > 0 ? TotalActivityCount / 7.0 : 0;

            DayStats = Enumerable.Range(0, 7)
                .Select(day =>
                {
                    int count = dayTotals.GetValueOrDefault(day);
                    return new DayActivityVm
                    {
                        DayOfWeek    = day,
                        DayName      = DayName(day),
                        Count        = count,
                        Coefficient  = averageDay > 0 ? count / averageDay : 0,
                        SharePercent = TotalActivityCount > 0 ? (double)count / TotalActivityCount * 100.0 : 0,
                        Intensity    = maxDay > 0 ? (double)count / maxDay : 0
                    };
                })
                .OrderByDescending(d => d.Count)
                .ToList();

            MostActiveDayLabel = DayStats.FirstOrDefault(d => d.Count > 0) is { } top
                ? $"{top.DayName} ({top.CoefficientLabel})"
                : LocalizationService.Instance["Common.NoData"];

            _distanceUnits = await _mouseProcessor.GetTotalDistanceUnits();

            OnPropertyChanged(nameof(HeatmapCells));
            OnPropertyChanged(nameof(DayStats));
            OnPropertyChanged(nameof(TotalActivityCount));
            OnPropertyChanged(nameof(MostActiveDayLabel));
            OnPropertyChanged(nameof(DistanceLabel));
            OnPropertyChanged(nameof(DistanceKm));
            OnPropertyChanged(nameof(DistanceM));
        }
        finally { IsLoading = false; }
    }

    private static string DayName(int day) => LocalizationService.Instance[$"Activity.Day{day}"];

    private static string BuildTooltip(int dayOfWeek, int hour, int count) =>
        $"{DayName(dayOfWeek)} {hour:00}:00 - {count} {LocalizationService.Instance["Activity.PressesShort"]}";

    private void RelocalizeLoadedData()
    {
        if (DayStats.Count > 0)
        {
            DayStats = DayStats.Select(day => new DayActivityVm
            {
                DayOfWeek = day.DayOfWeek,
                DayName = DayName(day.DayOfWeek),
                Count = day.Count,
                Coefficient = day.Coefficient,
                SharePercent = day.SharePercent,
                Intensity = day.Intensity
            }).ToList();

            MostActiveDayLabel = DayStats.FirstOrDefault(d => d.Count > 0) is { } top
                ? $"{top.DayName} ({top.CoefficientLabel})"
                : LocalizationService.Instance["Common.NoData"];

            OnPropertyChanged(nameof(DayStats));
        }

        if (HeatmapCells.Count > 0)
        {
            HeatmapCells = HeatmapCells.Select(cell => new HourCellVm
            {
                DayOfWeek = cell.DayOfWeek,
                Hour = cell.Hour,
                Count = cell.Count,
                Intensity = cell.Intensity,
                Tooltip = BuildTooltip(cell.DayOfWeek, cell.Hour, cell.Count)
            }).ToList();

            OnPropertyChanged(nameof(HeatmapCells));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
