using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ClickStat.Core.Interfaces;
using ClickStat.Presentation.Model;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace ClickStat.Presentation.ViewModels;

 public class StatisticsViewModel : INotifyPropertyChanged
    {
        private readonly IGetDataClick _dataClickService;
        private bool _isLoading;

        public bool IsLoading { get => _isLoading; set { _isLoading = value; OnPropertyChanged(); } }
        
        private int _totalClicks;
        public int TotalClicks { get => _totalClicks; set { _totalClicks = value; OnPropertyChanged(); } }

        private int _clicksToday;
        public int ClicksToday { get => _clicksToday; set { _clicksToday = value; OnPropertyChanged(); } }

        private string _mostFrequentKey;
        public string MostFrequentKey { get => _mostFrequentKey; set { _mostFrequentKey = value; OnPropertyChanged(); } }
        
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

        private async Task LoadStatsAsync()
        {
            IsLoading = true;

            // Загрузка данных для карточек
            TotalClicks = await _dataClickService.GetKeyStatisticsForTheAllTime();
            var todayStats = await _dataClickService.GetKeyStatisticsForTheDay(DateTime.Today);
            ClicksToday = todayStats.FirstOrDefault()?.ClickCount ?? 0;
            
            var allKeys = await _dataClickService.GetKeyStatistics();
            MostFrequentKey = allKeys.Any() ? allKeys.OrderByDescending(k => k.Count).First().KeyName : "N/A";

            // Загрузка данных для графика за последние 10 дней
            var dates = Enumerable.Range(0, 10).Select(i => DateTime.Today.AddDays(-i)).Reverse().ToList();
            var counts = new List<int>();
            foreach (var date in dates)
            {
                var dayStat = await _dataClickService.GetKeyStatisticsForTheDay(date);
                counts.Add(dayStat.FirstOrDefault()?.ClickCount ?? 0);
            }

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