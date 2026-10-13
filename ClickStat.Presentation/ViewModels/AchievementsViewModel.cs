using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ClickStat.Presentation.Services;

namespace ClickStat.Presentation.ViewModels;

public sealed class AchievementCard
{
    public string Icon { get; init; } = "";
    public string Category { get; init; } = "";
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
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

    public async Task LoadAsync()
    {
        if (IsLoading) return;
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
                    Icon = item.Icon,
                    Category = loc["Achievements." + item.Category],
                    Title = loc[item.TitleKey],
                    Description = loc[item.DescriptionKey],
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

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
