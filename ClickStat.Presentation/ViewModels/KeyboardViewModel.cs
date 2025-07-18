using System.ComponentModel;
using System.Runtime.CompilerServices;
using ClickStat.Core.Interfaces;

namespace ClickStat.Presentation.ViewModels;

public class KeyboardViewModel : INotifyPropertyChanged
{
    private readonly IGetDataClick _dataClickService;
    private Dictionary<string, int> _keyCounts = new Dictionary<string, int>();
    private bool _isLoading = true;

    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            _isLoading = value;
            OnPropertyChanged();
        }
    }

    public KeyboardViewModel(IGetDataClick dataClickService)
    {
        _dataClickService = dataClickService;
        _ = LoadKeyCountsAsync();
    }

    private async Task LoadKeyCountsAsync()
    {
        IsLoading = true;
        var stats = await _dataClickService.GetKeyStatistics();
        _keyCounts = stats.ToDictionary(s => s.KeyName, s => s.Count);
            
        OnPropertyChanged(string.Empty); 
        IsLoading = false;
    }

    public int GetCount(string keyName)
    {
        return _keyCounts.TryGetValue(keyName, out var count) ? count : 0;
    }
        
    public int this[string keyName] => GetCount(keyName);

    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}