using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using ClickStat.Core.Interfaces;
using ClickStat.Presentation.Model;

namespace ClickStat.Presentation.ViewModels;

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

    public ObservableCollection<CustomKeyItem> CustomKeys { get; private set; } = new();

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
        MaxCount  = KeyCounts.Values.Any() ? KeyCounts.Values.Max() : 1;

        // Keys tracked but not in the standard visual layout
        var custom = stats
            .Where(s => !StandardKeys.Contains(s.KeyName) && s.Count > 0)
            .OrderByDescending(s => s.Count)
            .Select(s => new CustomKeyItem { KeyName = s.KeyName, Count = s.Count });

        CustomKeys = new ObservableCollection<CustomKeyItem>(custom);

        OnPropertyChanged(string.Empty);
        IsLoading = false;
    }

    // All key names shown in the XAML keyboard layout
    private static readonly HashSet<string> StandardKeys = new()
    {
        "Escape",
        "F1","F2","F3","F4","F5","F6","F7","F8","F9","F10","F11","F12",
        "Oemtilde","D1","D2","D3","D4","D5","D6","D7","D8","D9","D0",
        "OemMinus","Oemplus","Back",
        "Tab","Q","W","E","R","T","Y","U","I","O","P","Oem4","Oem6","OemPipe",
        "Capital","A","S","D","F","G","H","J","K","L","OemSemicolon","OemQuotes","Enter",
        "LShiftKey","Z","X","C","V","B","N","M","Oemcomma","OemPeriod","Oem2","RShiftKey",
        "LControlKey","LWin","LMenu","Space","RMenu","RControlKey",
        "PrintScreen","LaunchApplication2","MediaStop",
        "Insert","Home","PageUp","Delete","End","Next",
        "Up","Left","Down","Right",
        "MediaPreviousTrack","MediaPlayPause","MediaNextTrack",
        "VolumeMute","VolumeDown","VolumeUp",
        "Clear","NumLock","Divide","Multiply","Subtract",
        "NumPad7","NumPad8","NumPad9",
        "NumPad4","NumPad5","NumPad6",
        "NumPad1","NumPad2","NumPad3",
        "NumPad0","Decimal","Add"
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
