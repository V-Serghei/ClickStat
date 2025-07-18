using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ClickStat.Core.Interfaces;
using ClickStat.Presentation.Model;

namespace ClickStat.Presentation.ViewModels;

public class StatisticsViewModel : INotifyPropertyChanged
    {
        private readonly IGetDataClick _dataClickService;
        private int _totalClicks;
        private bool _isLoading = true;

        public ObservableCollection<DailyStat> Last10DaysStats { get; } = new ObservableCollection<DailyStat>();

        public int TotalClicks
        {
            get => _totalClicks;
            set
            {
                _totalClicks = value;
                OnPropertyChanged();
            }
        }
        
        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                _isLoading = value;
                OnPropertyChanged();
            }
        }


        public StatisticsViewModel(IGetDataClick dataClickService)
        {
            _dataClickService = dataClickService;
            LoadDataAsync();
        }

        private async Task LoadDataAsync()
        {
            IsLoading = true;
            try
            {
                TotalClicks = await _dataClickService.GetKeyStatisticsForTheAllTime();

                Last10DaysStats.Clear();
                for (int i = 0; i < 10; i++)
                {
                    var date = DateTime.Today.AddDays(-i);
                    var statsForDay = await _dataClickService.GetKeyStatisticsForTheDay(date);
                    var dayData = statsForDay.FirstOrDefault();

                    Last10DaysStats.Add(new DailyStat
                    {
                        Date = date.ToString("dd.MM.yyyy"),
                        Count = dayData?.ClickCount ?? 0
                    });
                }
            }
            finally
            {
                IsLoading = false;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }