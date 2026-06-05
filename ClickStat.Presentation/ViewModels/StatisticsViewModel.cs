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

namespace ClickStat.Presentation.ViewModels
{
    public class StatisticsViewModel : INotifyPropertyChanged
    {
        private readonly IGetDataClick _dataClickService;
        private bool _isLoading;

        public bool IsLoading { get => _isLoading; set { _isLoading = value; OnPropertyChanged(); } }
        
        private int _totalClicks;
        public int TotalClicks { get => _totalClicks;
            private set { _totalClicks = value; OnPropertyChanged(); } }

        private int _clicksToday;
        public int ClicksToday { get => _clicksToday;
            private set { _clicksToday = value; OnPropertyChanged(); } }

        private string _mostFrequentKey;
        public string MostFrequentKey { get => _mostFrequentKey;
            private set { _mostFrequentKey = value; OnPropertyChanged(); } }
        
        public ISeries[] DailyStatsSeries { get; set; }
        public Axis[] XAxes { get; set; }
        public Axis[] YAxes { get; set; }
        public SolidColorPaint LegendTextPaint { get; set; }
        public SolidColorPaint TooltipBackgroundPaint { get; set; }
        public SolidColorPaint TooltipTextPaint { get; set; }

        public StatisticsViewModel(IGetDataClick dataClickService)
        {
            _dataClickService = dataClickService;
            InitializeChart();
            _ = LoadStatsAsync();
        }

        private void InitializeChart()
        {
            var textColor = new SKColor(200, 200, 200);
            var separatorColor = new SKColor(100, 100, 100);

            DailyStatsSeries = new ISeries[] { new ColumnSeries<int> { Name = "Нажатия", Values = new int[] { }, Fill = new SolidColorPaint(new SKColor(170, 112, 255)) } };
            
            XAxes = new Axis[]
            {
                new Axis { Name = "Дата", LabelsRotation = 15, TextSize = 12, NamePaint = new SolidColorPaint(textColor), LabelsPaint = new SolidColorPaint(textColor), SeparatorsPaint = new SolidColorPaint(separatorColor) }
            };

            YAxes = new Axis[]
            {
                new Axis { Name = "Количество нажатий", TextSize = 12, NamePaint = new SolidColorPaint(textColor), LabelsPaint = new SolidColorPaint(textColor), SeparatorsPaint = new SolidColorPaint(separatorColor) }
            };

            LegendTextPaint = new SolidColorPaint(textColor);
            TooltipBackgroundPaint = new SolidColorPaint(new SKColor(40, 40, 40));
            TooltipTextPaint = new SolidColorPaint(textColor);
        }

        public async Task LoadStatsAsync()
        {
            IsLoading = true;

            TotalClicks = await _dataClickService.GetKeyStatisticsForTheAllTime();
            var todayStats = await _dataClickService.GetKeyStatisticsForTheDay(DateTime.Today);
            ClicksToday = todayStats.FirstOrDefault()?.ClickCount ?? 0;
            
            var allKeys = await _dataClickService.GetKeyStatistics();
            MostFrequentKey = allKeys.Any() ? allKeys.OrderByDescending(k => k.Count).First().KeyName : "N/A";

            // Single batch query instead of 10 sequential ones
            var dates  = Enumerable.Range(0, 10).Select(i => DateTime.Today.AddDays(-9 + i)).ToList();
            var lookup = await _dataClickService.GetDailyClickCounts(dates[0], dates[^1]);
            var counts = dates.Select(d => lookup.TryGetValue(d.Date, out var c) ? c : 0).ToList();

            DailyStatsSeries[0].Values = counts;
            XAxes[0].Labels = dates.Select(d => d.ToString("dd MMM")).ToArray();

            IsLoading = false;
        }
        
        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}