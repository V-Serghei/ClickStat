using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;
using ClickStat.Presentation.ViewModels;

namespace ClickStat.Presentation.Services;

public sealed class ThemeService : INotifyPropertyChanged
{
    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "KeyClick");
    private static readonly string ThemeFile = Path.Combine(SettingsDir, "ui-theme.txt");

    private bool _isLightTheme;

    public static ThemeService Instance { get; } = new();

    public bool IsLightTheme
    {
        get => _isLightTheme;
        set
        {
            if (_isLightTheme == value)
                return;

            _isLightTheme = value;
            SaveTheme();
            OnPropertyChanged();
            OnPropertyChanged(nameof(ThemeIcon));
            OnPropertyChanged(nameof(WindowBackground));
            OnPropertyChanged(nameof(WindowBorder));
            OnPropertyChanged(nameof(TitleBarBackground));
            OnPropertyChanged(nameof(PanelBackground));
            OnPropertyChanged(nameof(CardBackground));
            OnPropertyChanged(nameof(CardBorder));
            OnPropertyChanged(nameof(PrimaryText));
            OnPropertyChanged(nameof(SecondaryText));
            OnPropertyChanged(nameof(MutedText));
        }
    }

    public string ThemeIcon => IsLightTheme ? "\uE708" : "\uE706"; // sun / moon in Segoe MDL2 Assets
    public ICommand ToggleThemeCommand { get; }

    public Brush WindowBackground => Brush(IsLightTheme ? "#F4F2FA" : "#12121E");
    public Brush WindowBorder => Brush(IsLightTheme ? "#D8D2EF" : "#1E1E40");
    public Brush TitleBarBackground => Brush(IsLightTheme ? "#ECE8F8" : "#0D0D1A");
    public Brush PanelBackground => Brush(IsLightTheme ? "#FAF9FE" : "#0E0E1C");
    public Brush CardBackground => Brush(IsLightTheme ? "#FFFFFF" : "#1A1A2E");
    public Brush CardBorder => Brush(IsLightTheme ? "#D7CFF0" : "#2E2E5E");
    public Brush PrimaryText => Brush(IsLightTheme ? "#201B33" : "#FFFFFF");
    public Brush SecondaryText => Brush(IsLightTheme ? "#5B5376" : "#C0C0E0");
    public Brush MutedText => Brush(IsLightTheme ? "#7A7296" : "#606080");

    private ThemeService()
    {
        _isLightTheme = string.Equals(LoadTheme(), "light", StringComparison.OrdinalIgnoreCase);
        ToggleThemeCommand = new RelayCommand(_ => IsLightTheme = !IsLightTheme);
    }

    private static Brush Brush(string color) =>
        new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));

    private static string? LoadTheme()
    {
        try
        {
            return File.Exists(ThemeFile) ? File.ReadAllText(ThemeFile).Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    private void SaveTheme()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            File.WriteAllText(ThemeFile, IsLightTheme ? "light" : "dark");
        }
        catch
        {
            // Theme selection is cosmetic; failing to save it must not break the app.
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
