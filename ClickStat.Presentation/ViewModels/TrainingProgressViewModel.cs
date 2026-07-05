using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ClickStat.Infrastructure.Data;
using ClickStat.Presentation.Services;

namespace ClickStat.Presentation.ViewModels;

public sealed class TrainingLanguageProgress
{
    public string Language { get; init; } = "";
    public int Sessions { get; init; }
    public double BestWpm { get; init; }
    public double AverageWpm { get; init; }
    public double AverageAccuracy { get; init; }
    public int TotalCorrectChars { get; init; }
    public int TotalErrors { get; init; }
}

public sealed class TrainingSessionChartItem
{
    public string Label { get; init; } = "";
    public double Wpm { get; init; }
    public double Accuracy { get; init; }
    public double RelativeHeight { get; init; }
}

public sealed class TrainingProgressViewModel : INotifyPropertyChanged
{
    private readonly TypingTrainingProcessor _processor;
    private readonly ITypingTrainerWindowService _typingTrainerWindowService;

    public TrainingProgressViewModel(
        TypingTrainingProcessor processor,
        ITypingTrainerWindowService typingTrainerWindowService)
    {
        _processor = processor;
        _typingTrainerWindowService = typingTrainerWindowService;
        _typingTrainerWindowService.TrainerClosed += async (_, _) => await LoadAsync();
        OpenTypingTrainerCommand = new RelayCommand(_ => OpenTypingTrainer());
    }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        private set { if (_isLoading == value) return; _isLoading = value; OnPropertyChanged(); }
    }

    private bool _hasData;
    public bool HasData
    {
        get => _hasData;
        private set { if (_hasData == value) return; _hasData = value; OnPropertyChanged(); }
    }

    public ObservableCollection<TrainingLanguageProgress> Languages { get; } = new();
    public ObservableCollection<TypingTrainingResult> RecentResults { get; } = new();
    public ObservableCollection<TrainingSessionChartItem> ChartItems { get; } = new();
    public ICommand OpenTypingTrainerCommand { get; }

    private void OpenTypingTrainer()
    {
        _typingTrainerWindowService.Show();
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var summariesTask = _processor.GetSummaryByLanguageAsync();
            var recentTask = _processor.GetRecentAsync();
            await Task.WhenAll(summariesTask, recentTask);

            Languages.Clear();
            foreach (var item in summariesTask.Result)
            {
                Languages.Add(new TrainingLanguageProgress
                {
                    Language = item.Key,
                    Sessions = item.Value.Sessions,
                    BestWpm = item.Value.BestWpm,
                    AverageWpm = item.Value.AverageWpm,
                    AverageAccuracy = item.Value.AverageAccuracy,
                    TotalCorrectChars = item.Value.TotalCorrectChars,
                    TotalErrors = item.Value.TotalErrors
                });
            }

            RecentResults.Clear();
            foreach (var result in recentTask.Result)
                RecentResults.Add(result);

            ChartItems.Clear();
            var chartSource = recentTask.Result.Reverse().TakeLast(18).ToList();
            var maxWpm = Math.Max(1, chartSource.Count == 0 ? 1 : chartSource.Max(x => x.Wpm));
            foreach (var result in chartSource)
            {
                ChartItems.Add(new TrainingSessionChartItem
                {
                    Label = result.Language,
                    Wpm = result.Wpm,
                    Accuracy = result.Accuracy,
                    RelativeHeight = Math.Max(8, Math.Round(result.Wpm / maxWpm * 120, 1))
                });
            }

            HasData = Languages.Count > 0 || RecentResults.Count > 0;
        }
        finally
        {
            IsLoading = false;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
