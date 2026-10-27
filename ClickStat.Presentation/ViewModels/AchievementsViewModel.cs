using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ClickStat.Presentation.Services;
using ClickStat.Presentation.Views;

namespace ClickStat.Presentation.ViewModels;

public sealed class AchievementCard
{
    public string Id { get; init; } = "";
    public string Icon { get; init; } = "";
    public string Category { get; init; } = "";
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string Detail { get; init; } = "";
    public string Status { get; init; } = "";
    public long Current { get; init; }
    public long Target { get; init; }
    public double Percent { get; init; }
    public bool IsUnlocked { get; init; }
    public string UnlockedDate { get; init; } = "";
    public string Counter => $"{Math.Min(Current, Target):N0} / {Target:N0}";
}

public sealed class AchievementsViewModel : INotifyPropertyChanged
{
    private readonly AchievementService _service;
    private bool _isLoading;
    private string _error = "";
    private int _unlockedCount;
    private Task _loadTask = Task.CompletedTask;
    private readonly Dictionary<string, AchievementDetailWindow> _detailWindows = new();
    public ObservableCollection<AchievementCard> Items { get; } = new();
    public ICommand RefreshCommand { get; }
    public bool IsLoading { get => _isLoading; private set { _isLoading = value; OnPropertyChanged(); } }
    public string Error { get => _error; private set { _error = value; OnPropertyChanged(); } }
    public int UnlockedCount { get => _unlockedCount; private set { _unlockedCount = value; OnPropertyChanged(); } }
    public int TotalCount => Items.Count;

    public AchievementsViewModel(AchievementService service)
    {
        _service = service;
        RefreshCommand = new RelayCommand(_ => _ = LoadAsync());
        LocalizationService.Instance.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is "Item[]" or nameof(LocalizationService.CurrentLanguage))
                _ = LoadAsync();
        };
    }

    public Task LoadAsync()
    {
        if (!_loadTask.IsCompleted) return _loadTask;
        _loadTask = LoadCoreAsync();
        return _loadTask;
    }

    private async Task LoadCoreAsync()
    {
        IsLoading = true;
        Error = "";
        try
        {
            var result = await _service.RefreshAsync();
            var loc = LocalizationService.Instance;
            Items.Clear();
            foreach (var item in result.OrderByDescending(x => x.IsUnlocked).ThenBy(x => x.Category).ThenBy(x => x.Target))
                Items.Add(new AchievementCard
                {
                    Id = item.Id,
                    Icon = item.Icon,
                    Category = loc["Achievements." + item.Category],
                    Title = loc[item.TitleKey],
                    Description = loc[item.DescriptionKey],
                    Detail = loc["Achievement." + item.Id + ".Detail"],
                    Status = item.IsUnlocked ? loc["Achievements.Earned"] : loc["Achievements.Locked"],
                    Current = item.Current,
                    Target = item.Target,
                    Percent = item.Percent,
                    IsUnlocked = item.IsUnlocked,
                    UnlockedDate = item.UnlockedAt?.LocalDateTime.ToString("d") ?? ""
                });
            UnlockedCount = Items.Count(x => x.IsUnlocked);
            OnPropertyChanged(nameof(TotalCount));
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally { IsLoading = false; }
    }

    public async Task OpenDetailAsync(string id, bool refresh = false)
    {
        if (refresh || Items.Count == 0 || !_loadTask.IsCompleted)
            await LoadAsync();
        var card = Items.FirstOrDefault(item => item.Id == id);
        if (card == null) return;
        if (_detailWindows.TryGetValue(id, out var existing) && existing.IsVisible)
        {
            existing.Activate();
            return;
        }
        var window = new AchievementDetailWindow
        {
            Owner = System.Windows.Application.Current.MainWindow,
            DataContext = card
        };
        window.Closed += (_, _) => _detailWindows.Remove(id);
        _detailWindows[id] = window;
        window.Show();
        window.Activate();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
