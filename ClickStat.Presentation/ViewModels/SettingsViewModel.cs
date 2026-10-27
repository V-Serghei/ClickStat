using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using ClickStat.Core.Interfaces;
using ClickStat.Core.Services;
using ClickStat.Infrastructure.Data;

namespace ClickStat.Presentation.ViewModels;

public class SettingsViewModel : INotifyPropertyChanged
{
    private readonly IStartupService _startupService;
    private readonly WordProcessor _wordProcessor;

    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "KeyClick");
    private static readonly string BgFile = Path.Combine(SettingsDir, "background.txt");

    // ── Injected by MainViewModel ──────────────────────────────────────────
    internal BreakReminderService? BreakReminder { get; set; }
    public event Action? WordHistoryDeleted;

    public bool CanDeleteWordHistory => !CaptureWordsAndPhrases;
    private bool _isDeletingWordHistory;
    public bool CanChangeWordCapture => !_isDeletingWordHistory;

    public bool CaptureWordsAndPhrases
    {
        get => _wordProcessor.CaptureEnabled;
        set
        {
            if (_isDeletingWordHistory) return;
            if (_wordProcessor.CaptureEnabled == value) return;
            _wordProcessor.CaptureEnabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanDeleteWordHistory));
        }
    }

    public async Task DeleteWordHistoryAsync()
    {
        if (!CanDeleteWordHistory || _isDeletingWordHistory) return;
        _isDeletingWordHistory = true;
        OnPropertyChanged(nameof(CanChangeWordCapture));
        try
        {
            await _wordProcessor.DeleteHistoryAsync();
            WordHistoryDeleted?.Invoke();
        }
        finally
        {
            _isDeletingWordHistory = false;
            OnPropertyChanged(nameof(CanChangeWordCapture));
        }
    }

    // ── Startup ───────────────────────────────────────────────────────────
    private string _startupError = "";
    public string StartupError
    {
        get => _startupError;
        private set { _startupError = value; OnPropertyChanged(); }
    }

    public bool IsInStartup
    {
        get => _startupService.IsInStartup();
        set
        {
            try
            {
                if (value) _startupService.AddToStartup();
                else _startupService.RemoveFromStartup();
                StartupError = "";
            }
            catch (Exception ex) { StartupError = ex.Message; }
            OnPropertyChanged();
        }
    }

    public void RefreshStartupState() => OnPropertyChanged(nameof(IsInStartup));

    // ── Break reminder ─────────────────────────────────────────────────────
    private bool _breakEnabled;
    public bool BreakEnabled
    {
        get => _breakEnabled;
        set
        {
            _breakEnabled = value;
            if (BreakReminder != null) BreakReminder.IsEnabled = value;
            OnPropertyChanged();
        }
    }

    private int _breakInterval = 45;
    public int BreakInterval
    {
        get => _breakInterval;
        set
        {
            _breakInterval = Math.Clamp(value, 5, 240);
            if (BreakReminder != null) BreakReminder.IntervalMinutes = _breakInterval;
            OnPropertyChanged();
        }
    }

    // ── Background image ──────────────────────────────────────────────────

    // Default = pack URI to built-in Assets/fill.jpg
    private const string DefaultBg = "pack://siteoforigin:,,,/Assets/fill.jpg";
    private static readonly string AssemblyBg =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "fill.jpg");

    private string _backgroundPath = "";
    public string BackgroundPath
    {
        get => _backgroundPath;
        set
        {
            _backgroundPath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(BackgroundUri));
            SaveBgPath();
        }
    }

    /// <summary>Resolved URI ready for ImageBrush.ImageSource binding.</summary>
    public string BackgroundUri =>
        !string.IsNullOrEmpty(_backgroundPath) && File.Exists(_backgroundPath)
            ? _backgroundPath
            : (File.Exists(AssemblyBg) ? AssemblyBg : "");

    public void ClearBackground()
    {
        _backgroundPath = "";
        OnPropertyChanged(nameof(BackgroundPath));
        OnPropertyChanged(nameof(BackgroundUri));
        SaveBgPath();
    }

    // ── Constructor ───────────────────────────────────────────────────────
    public SettingsViewModel(IStartupService startupService, WordProcessor wordProcessor)
    {
        _startupService = startupService;
        _wordProcessor = wordProcessor;
        LoadBgPath();
    }

    // ── Persistence ───────────────────────────────────────────────────────
    private void LoadBgPath()
    {
        try
        {
            if (File.Exists(BgFile))
                _backgroundPath = File.ReadAllText(BgFile).Trim();
        }
        catch { /* ignore */ }
    }

    private void SaveBgPath()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            File.WriteAllText(BgFile, _backgroundPath);
        }
        catch { /* ignore */ }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
