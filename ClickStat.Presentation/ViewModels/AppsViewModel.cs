using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using ClickStat.Infrastructure.Data;
using ClickStat.Infrastructure.Data.Model;

namespace ClickStat.Presentation.ViewModels;

public class AppsViewModel : INotifyPropertyChanged
{
    private readonly AppUsageProcessor _appProcessor;

    private bool _isLoading;
    public bool IsLoading { get => _isLoading; set { _isLoading = value; OnPropertyChanged(); } }

    public ObservableCollection<AppUsageStatistics> Apps { get; } = new();

    public AppsViewModel(AppUsageProcessor appProcessor)
    {
        _appProcessor = appProcessor;
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        await Task.Yield();
        try
        {
            var list = await Task.Run(async () => await _appProcessor.GetTopApps(30));
            Apps.Clear();
            foreach (var app in list) Apps.Add(app);
        }
        finally { IsLoading = false; }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
