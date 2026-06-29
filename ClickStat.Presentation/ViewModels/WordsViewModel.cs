using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using System.Windows.Input;
using ClickStat.Infrastructure.Data;
using ClickStat.Infrastructure.Data.Model;
using ClickStat.Presentation.Services;

namespace ClickStat.Presentation.ViewModels;

public class WordsViewModel : INotifyPropertyChanged
{
    private readonly WordProcessor _wordProcessor;

    private bool _isLoading;
    public bool IsLoading { get => _isLoading; set { _isLoading = value; OnPropertyChanged(); } }

    public ObservableCollection<WordStatistics> TopWords { get; } = new();
    public ObservableCollection<WordPhrase> TopPhrases { get; } = new();
    public ObservableCollection<WordLanguageOption> Languages { get; } = new();
    public LocalizationService Loc => LocalizationService.Instance;

    private int _totalTyped;
    private int _uniqueCount;
    private int _phraseCount;
    private WordLanguageOption? _selectedLanguage;

    private List<WordStatistics> _allWords = new();
    private List<WordPhrase> _allPhrases = new();

    public int TotalTyped { get => _totalTyped; set { _totalTyped = value; OnPropertyChanged(); } }
    public int UniqueCount { get => _uniqueCount; set { _uniqueCount = value; OnPropertyChanged(); } }
    public int PhraseCount { get => _phraseCount; set { _phraseCount = value; OnPropertyChanged(); } }

    public WordLanguageOption? SelectedLanguage
    {
        get => _selectedLanguage;
        private set
        {
            if (_selectedLanguage == value) return;
            _selectedLanguage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LanguageTitle));
        }
    }

    public string LanguageTitle => SelectedLanguage?.DisplayName ?? Loc["Words.NoLayouts"];

    public ICommand SelectLanguageCommand { get; }

    public WordsViewModel(WordProcessor wordProcessor)
    {
        _wordProcessor = wordProcessor;
        SelectLanguageCommand = new RelayCommand(p =>
        {
            if (p is WordLanguageOption option)
                SetLanguage(option);
        });
        RefreshLanguageOptions();
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        await Task.Yield();
        try
        {
            var loaded = await Task.Run(async () =>
            {
                await _wordProcessor.FlushAsync();
                var words = await _wordProcessor.GetTopWords(1000);
                var phrases = await _wordProcessor.GetTopPhrases(300);
                return (Words: words, Phrases: phrases);
            });

            _allWords = loaded.Words;
            _allPhrases = loaded.Phrases;
            RefreshLanguageOptions();
            ApplyLanguageFilter();
        }
        finally { IsLoading = false; }
    }

    public void BeginLoading() => IsLoading = true;

    private void SetLanguage(WordLanguageOption language)
    {
        SelectedLanguage = language;
        ApplyLanguageFilter();
    }

    private void RefreshLanguageOptions()
    {
        var existing = SelectedLanguage?.Code;
        Languages.Clear();

        foreach (var culture in GetInstalledCultures())
            Languages.Add(new WordLanguageOption(culture));

        SelectedLanguage = Languages.FirstOrDefault(x => x.Code == existing)
            ?? Languages.FirstOrDefault(x => x.Code.Equals("RU", StringComparison.OrdinalIgnoreCase))
            ?? Languages.FirstOrDefault()
            ?? new WordLanguageOption(CultureInfo.GetCultureInfo("en-US"));
    }

    private void ApplyLanguageFilter()
    {
        var selected = SelectedLanguage;
        var words = selected == null
            ? new List<WordStatistics>()
            : _allWords
                .Where(w => selected.MatchesWord(w.Word))
                .OrderByDescending(w => w.Count)
                .ToList();

        var phrases = selected == null
            ? new List<WordPhrase>()
            : _allPhrases
                .Where(p => selected.MatchesPhrase(p.Phrase))
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

    private static IEnumerable<CultureInfo> GetInstalledCultures()
    {
        return InputLanguage.InstalledInputLanguages
            .Cast<InputLanguage>()
            .Select(x => x.Culture)
            .GroupBy(x => x.TwoLetterISOLanguageName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(x => x.NativeName);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public sealed class WordLanguageOption
{
    public string Code { get; }
    public string DisplayName { get; }
    public string WordGroup { get; }

    public WordLanguageOption(CultureInfo culture)
    {
        Code = culture.TwoLetterISOLanguageName.ToUpperInvariant();
        DisplayName = culture.NativeName.Length > 0
            ? culture.NativeName[..1].ToUpper(culture) + culture.NativeName[1..]
            : Code;
        WordGroup = Code is "RU" or "UK" or "BG" or "SR" or "BE" ? "RU" : "EN";
    }

    public bool MatchesWord(string word) => WordProcessor.GetWordLanguage(word) == WordGroup;

    public bool MatchesPhrase(string phrase) => WordProcessor.IsPhraseLanguage(phrase, WordGroup);
}
