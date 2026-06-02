using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using ClickStat.Infrastructure.Data;
using ClickStat.Infrastructure.Data.Model;

namespace ClickStat.Presentation.ViewModels;

public class WordsViewModel : INotifyPropertyChanged
{
    private readonly WordProcessor _wordProcessor;

    private bool _isLoading;
    public bool IsLoading { get => _isLoading; set { _isLoading = value; OnPropertyChanged(); } }

    public ObservableCollection<WordStatistics>    TopWords     { get; } = new();
    public ObservableCollection<WordStatistics>    KnownWords   { get; } = new();
    public ObservableCollection<WordStatistics>    UniqueWords  { get; } = new();
    public ObservableCollection<KeyBigram>         TopBigrams   { get; } = new();
    public ObservableCollection<WordPhrase>        TopPhrases   { get; } = new();

    private int    _totalTyped;
    private int    _uniqueCount;
    private double _knownPercent;

    public int    TotalTyped    { get => _totalTyped;    set { _totalTyped = value;    OnPropertyChanged(); } }
    public int    UniqueCount   { get => _uniqueCount;   set { _uniqueCount = value;   OnPropertyChanged(); } }
    public double KnownPercent  { get => _knownPercent;  set { _knownPercent = value;  OnPropertyChanged(); } }

    public WordsViewModel(WordProcessor wordProcessor)
    {
        _wordProcessor = wordProcessor;
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var words   = await _wordProcessor.GetTopWords(100);
            var bigrams = await _wordProcessor.GetTopBigrams(30);
            var phrases = await _wordProcessor.GetTopPhrases(30);

            TotalTyped  = await _wordProcessor.GetTotalWordsTyped();
            UniqueCount = await _wordProcessor.GetUniqueWordsCount();

            int knownCount = words.Count(w => w.IsKnown);
            KnownPercent   = words.Count > 0 ? System.Math.Round((double)knownCount / words.Count * 100, 1) : 0;

            TopWords.Clear();
            KnownWords.Clear();
            UniqueWords.Clear();

            foreach (var w in words)
            {
                TopWords.Add(w);
                if (w.IsKnown)   KnownWords.Add(w);
                else             UniqueWords.Add(w);
            }

            TopBigrams.Clear();
            foreach (var b in bigrams) TopBigrams.Add(b);

            TopPhrases.Clear();
            foreach (var p in phrases) TopPhrases.Add(p);
        }
        finally { IsLoading = false; }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
