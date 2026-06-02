using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using ClickStat.Infrastructure.Data;
using ClickStat.Infrastructure.Data.Model;

namespace ClickStat.Presentation.ViewModels;

public class WordsViewModel : INotifyPropertyChanged
{
    private readonly WordProcessor _wordProcessor;

    private bool _isLoading;
    public bool IsLoading { get => _isLoading; set { _isLoading = value; OnPropertyChanged(); } }

    public ObservableCollection<WordStatistics>    TopWords     { get; } = new();
    public ObservableCollection<WordPhrase>        TopPhrases   { get; } = new();

    private int    _totalTyped;
    private int    _uniqueCount;
    private int    _phraseCount;
    private string _selectedLanguage = "RU";

    private List<WordStatistics> _allWords = new();
    private List<WordPhrase> _allPhrases = new();

    public int    TotalTyped    { get => _totalTyped;    set { _totalTyped = value;    OnPropertyChanged(); } }
    public int    UniqueCount   { get => _uniqueCount;   set { _uniqueCount = value;   OnPropertyChanged(); } }
    public int    PhraseCount   { get => _phraseCount;   set { _phraseCount = value;   OnPropertyChanged(); } }
    public string SelectedLanguage
    {
        get => _selectedLanguage;
        private set
        {
            if (_selectedLanguage == value) return;
            _selectedLanguage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LanguageTitle));
            OnPropertyChanged(nameof(RussianButtonText));
            OnPropertyChanged(nameof(EnglishButtonText));
        }
    }

    public string LanguageTitle => SelectedLanguage == "RU" ? "Русский" : "English";
    public string RussianButtonText => SelectedLanguage == "RU" ? "● Русский" : "Русский";
    public string EnglishButtonText => SelectedLanguage == "EN" ? "● English" : "English";

    public ICommand ShowRussianCommand { get; }
    public ICommand ShowEnglishCommand { get; }

    public WordsViewModel(WordProcessor wordProcessor)
    {
        _wordProcessor = wordProcessor;
        ShowRussianCommand = new RelayCommand(_ => SetLanguage("RU"));
        ShowEnglishCommand = new RelayCommand(_ => SetLanguage("EN"));
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            _allWords = await _wordProcessor.GetTopWords(1000);
            _allPhrases = await _wordProcessor.GetTopPhrases(300);
            ApplyLanguageFilter();
        }
        finally { IsLoading = false; }
    }

    private void SetLanguage(string language)
    {
        SelectedLanguage = language;
        ApplyLanguageFilter();
    }

    private void ApplyLanguageFilter()
    {
        var words = _allWords
            .Where(w => WordProcessor.GetWordLanguage(w.Word) == SelectedLanguage)
            .OrderByDescending(w => w.Count)
            .ToList();

        var phrases = _allPhrases
            .Where(p => WordProcessor.IsPhraseLanguage(p.Phrase, SelectedLanguage))
            .OrderByDescending(p => p.Count)
            .ToList();

        TopWords.Clear();
        foreach (var word in words.Take(120)) TopWords.Add(word);

        TopPhrases.Clear();
        foreach (var phrase in phrases.Take(60)) TopPhrases.Add(phrase);

        TotalTyped = words.Sum(w => w.Count);
        UniqueCount = words.Count;
        PhraseCount = phrases.Count;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
