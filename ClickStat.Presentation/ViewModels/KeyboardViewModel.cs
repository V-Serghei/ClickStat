using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using ClickStat.Core.Interfaces;

namespace ClickStat.Presentation.ViewModels
{
    public class KeyboardViewModel : INotifyPropertyChanged
    {
        private readonly IGetDataClick _dataClickService;
        private bool _isLoading = true;

        public bool IsLoading
        {
            get => _isLoading;
            set { _isLoading = value; OnPropertyChanged(); }
        }

        public Dictionary<string, int> KeyCounts { get; private set; } = new();

        private int _maxCount;
        public int MaxCount { get => _maxCount; set { _maxCount = value; OnPropertyChanged(); } }

        public KeyboardViewModel(IGetDataClick dataClickService)
        {
            _dataClickService = dataClickService;
            _ = LoadKeyCountsAsync();
        }

        private async Task LoadKeyCountsAsync()
        {
            IsLoading = true;
            // Имитация задержки сети для демонстрации индикатора загрузки
            await Task.Delay(500); 

            var stats = await _dataClickService.GetKeyStatistics();
            KeyCounts = stats.ToDictionary(s => s.KeyName, s => s.Count);

            MaxCount = KeyCounts.Values.Any() ? KeyCounts.Values.Max() : 1; // Избегаем деления на ноль
            
            OnPropertyChanged(string.Empty); // Уведомить UI обо всех изменениях
            IsLoading = false;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
    
    // Вспомогательный конвертер для инвертирования Visibility
    [ValueConversion(typeof(bool), typeof(Visibility))]
    public class InverseBooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool boolValue = (bool)value;
            if (parameter as string == "inverse")
                boolValue = !boolValue;
            return boolValue ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}