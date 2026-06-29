using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Data;
using System.Windows.Input;

namespace ClickStat.Presentation.Services;

public sealed class LocalizationService : INotifyPropertyChanged
{
    private readonly Dictionary<string, Dictionary<string, string>> _languages = new(StringComparer.OrdinalIgnoreCase);
    private string _currentLanguage = "ru";

    public static LocalizationService Instance { get; } = new();

    public ObservableCollection<UiLanguageOption> Languages { get; } = new();

    public string CurrentLanguage
    {
        get => _currentLanguage;
        private set
        {
            if (_currentLanguage == value)
                return;

            _currentLanguage = value;
            OnPropertyChanged();
            OnPropertyChanged("Item[]");
            OnPropertyChanged(nameof(CurrentLanguageLabel));
        }
    }

    public string CurrentLanguageLabel =>
        Languages.FirstOrDefault(x => x.Code.Equals(CurrentLanguage, StringComparison.OrdinalIgnoreCase))?.DisplayName
        ?? CurrentLanguage.ToUpperInvariant();

    public string this[string key]
    {
        get
        {
            if (_languages.TryGetValue(CurrentLanguage, out var current) && current.TryGetValue(key, out var value))
                return value;

            if (_languages.TryGetValue("ru", out var fallback) && fallback.TryGetValue(key, out var fallbackValue))
                return fallbackValue;

            return key;
        }
    }

    private LocalizationService()
    {
        LoadLanguages();
    }

    public void SetLanguage(string code)
    {
        if (_languages.ContainsKey(code))
            CurrentLanguage = code;
    }

    private void LoadLanguages()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Localization");
        if (!Directory.Exists(folder))
            return;

        foreach (var file in Directory.EnumerateFiles(folder, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var map = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (map == null)
                    continue;

                var code = Path.GetFileNameWithoutExtension(file);
                _languages[code] = map;
                Languages.Add(new UiLanguageOption(code, map.GetValueOrDefault("Language.Name", code.ToUpperInvariant())));
            }
            catch
            {
                // Broken localization files should not block app startup.
            }
        }

        if (_languages.ContainsKey("ru"))
            _currentLanguage = "ru";
        else if (_languages.Count > 0)
            _currentLanguage = _languages.Keys.First();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed record UiLanguageOption(string Code, string DisplayName);

public sealed class LocExtension : Binding
{
    public LocExtension(string key)
    {
        Source = LocalizationService.Instance;
        Path = new System.Windows.PropertyPath($"[{key}]");
        Mode = BindingMode.OneWay;
        UpdateSourceTrigger = UpdateSourceTrigger.Explicit;
    }
}

public sealed class SetUiLanguageCommand : ICommand
{
    public bool CanExecute(object? parameter) => parameter is string or UiLanguageOption;

    public void Execute(object? parameter)
    {
        var code = parameter switch
        {
            UiLanguageOption option => option.Code,
            string value => value,
            _ => null
        };

        if (!string.IsNullOrWhiteSpace(code))
            LocalizationService.Instance.SetLanguage(code);
    }

    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }
}
