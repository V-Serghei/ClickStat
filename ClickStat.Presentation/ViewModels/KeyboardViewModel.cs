using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
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

        public async Task LoadKeyCountsAsync()
        {
            IsLoading = true;
            
            await Task.Delay(500); 

            var stats = await _dataClickService.GetKeyStatistics();
            KeyCounts = stats.ToDictionary(s => s.KeyName, s => s.Count);

            MaxCount = KeyCounts.Values.Any() ? KeyCounts.Values.Max() : 1;
            
            OnPropertyChanged(string.Empty);
            IsLoading = false;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}