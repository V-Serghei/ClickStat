using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Threading;
using ClickStat.Infrastructure.Data;
using ClickStat.Infrastructure.Services;
using ClickStat.Presentation.Services;

namespace ClickStat.Presentation.ViewModels;

public sealed class TrainingKeyStat : INotifyPropertyChanged
{
    public string KeyName { get; init; } = "";

    private int _count;
    public int Count
    {
        get => _count;
        set
        {
            if (_count == value) return;
            _count = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class TypingTrainerViewModel : INotifyPropertyChanged, IDisposable
{
    private const int PreviewPageLength = 48;
    private readonly LiveEventBus _liveBus;
    private readonly TypingTrainingProcessor _processor;
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _keyFlashTimer;
    private readonly DispatcherTimer _layoutTimer;
    private readonly Dictionary<string, TrainingKeyStat> _keyStats = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _startedAt;
    private TimeSpan _pausedElapsed;
    private bool _disposed;
    private bool _resultSaved;
    private bool _isFinishing;

    public event Action<TrainingSessionResult>? SessionFinished;

    public TypingTrainerViewModel(LiveEventBus liveBus, TypingTrainingProcessor processor)
    {
        _liveBus = liveBus;
        _processor = processor;
        StartCommand = new RelayCommand(_ => Start());
        PauseCommand = new RelayCommand(_ => TogglePause());
        ResetCommand = new RelayCommand(_ => Reset());
        FinishCommand = new RelayCommand(async _ => await FinishAsync());

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => RefreshTiming();
        _keyFlashTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
        _keyFlashTimer.Tick += (_, _) =>
        {
            _keyFlashTimer.Stop();
            ActiveKeyName = "";
            ActiveKeyIsError = false;
        };
        _layoutTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _layoutTimer.Tick += (_, _) => UpdateLayoutInfo();
        _layoutTimer.Start();
        UpdateLayoutInfo(force: true);
        _liveBus.KeyInput += OnKeyInput;
        UpdatePreview();
    }

    private string _sourceText = "";
    public string SourceText
    {
        get => _sourceText;
        set
        {
            if (_sourceText == value) return;
            _sourceText = (value ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanStart));
            if (!IsRunning && !IsPaused)
                ResetProgressOnly();
        }
    }

    private string _typedPrefix = "";
    public string TypedPrefix
    {
        get => _typedPrefix;
        private set { _typedPrefix = value; OnPropertyChanged(); }
    }

    private string _currentCharacter = "";
    public string CurrentCharacter
    {
        get => _currentCharacter;
        private set { _currentCharacter = value; OnPropertyChanged(); }
    }

    private string _currentSpace = "";
    public string CurrentSpace
    {
        get => _currentSpace;
        private set { _currentSpace = value; OnPropertyChanged(); }
    }

    private string _upcomingText = "";
    public string UpcomingText
    {
        get => _upcomingText;
        private set { _upcomingText = value; OnPropertyChanged(); }
    }

    private string _status = "";
    public string Status
    {
        get => _status;
        private set { _status = value; OnPropertyChanged(); }
    }

    private string _lastError = "";
    public string LastError
    {
        get => _lastError;
        private set { _lastError = value; OnPropertyChanged(); }
    }

    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (_isRunning == value) return;
            _isRunning = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanStart));
            OnPropertyChanged(nameof(IsTextLocked));
        }
    }

    private bool _isPaused;
    public bool IsPaused
    {
        get => _isPaused;
        private set
        {
            if (_isPaused == value) return;
            _isPaused = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsTextLocked));
        }
    }

    private int _position;
    public int Position
    {
        get => _position;
        private set
        {
            if (_position == value) return;
            _position = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ProgressPercent));
            OnPropertyChanged(nameof(RemainingCharacters));
        }
    }

    private int _correctChars;
    public int CorrectChars
    {
        get => _correctChars;
        private set
        {
            if (_correctChars == value) return;
            _correctChars = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Accuracy));
        }
    }

    private int _errors;
    public int Errors
    {
        get => _errors;
        private set
        {
            if (_errors == value) return;
            _errors = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Accuracy));
        }
    }

    private double _cpm;
    public double Cpm
    {
        get => _cpm;
        private set { if (Math.Abs(_cpm - value) < 0.01) return; _cpm = value; OnPropertyChanged(); }
    }

    private double _wpm;
    public double Wpm
    {
        get => _wpm;
        private set { if (Math.Abs(_wpm - value) < 0.01) return; _wpm = value; OnPropertyChanged(); }
    }

    private int _elapsedSeconds;
    public int ElapsedSeconds
    {
        get => _elapsedSeconds;
        private set { if (_elapsedSeconds == value) return; _elapsedSeconds = value; OnPropertyChanged(); }
    }

    public int TotalCharacters => SourceText.Length;
    public int RemainingCharacters => Math.Max(0, TotalCharacters - Position);
    public double ProgressPercent => TotalCharacters == 0 ? 0 : Math.Round(Position * 100.0 / TotalCharacters, 1);
    public double Accuracy => CorrectChars + Errors == 0 ? 100 : Math.Round(CorrectChars * 100.0 / (CorrectChars + Errors), 1);
    public bool CanStart => !IsRunning && !string.IsNullOrEmpty(SourceText);
    public bool IsTextLocked => IsRunning || IsPaused;
    public string DetectedLanguage => DetectLanguage(SourceText);
    public ObservableCollection<TrainingKeyStat> KeyStats { get; } = new();
    public Dictionary<string, int> SessionKeyCounts { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

    private string _layoutCode = "EN";
    public string LayoutCode
    {
        get => _layoutCode;
        private set { if (_layoutCode == value) return; _layoutCode = value; OnPropertyChanged(); }
    }

    private string _activeKeyName = "";
    public string ActiveKeyName
    {
        get => _activeKeyName;
        private set { if (_activeKeyName == value) return; _activeKeyName = value; OnPropertyChanged(); }
    }

    private bool _activeKeyIsError;
    public bool ActiveKeyIsError
    {
        get => _activeKeyIsError;
        private set { if (_activeKeyIsError == value) return; _activeKeyIsError = value; OnPropertyChanged(); }
    }

    public ICommand StartCommand { get; }
    public ICommand PauseCommand { get; }
    public ICommand ResetCommand { get; }
    public ICommand FinishCommand { get; }

    private void Start()
    {
        if (!CanStart) return;

        ResetProgressOnly();
        _resultSaved = false;
        _startedAt = DateTime.Now;
        _pausedElapsed = TimeSpan.Zero;
        IsRunning = true;
        IsPaused = false;
        Status = "Training started";
        _timer.Start();
    }

    private void TogglePause()
    {
        if (!IsRunning && !IsPaused) return;

        if (IsRunning)
        {
            RefreshTiming();
            _pausedElapsed = TimeSpan.FromSeconds(ElapsedSeconds);
            IsRunning = false;
            IsPaused = true;
            _timer.Stop();
            Status = "Paused";
        }
        else
        {
            _startedAt = DateTime.Now;
            IsRunning = true;
            IsPaused = false;
            _timer.Start();
            Status = "Training resumed";
        }
    }

    private void Reset()
    {
        IsRunning = false;
        IsPaused = false;
        _timer.Stop();
        ResetProgressOnly();
        Status = "";
    }

    private async Task FinishAsync()
    {
        if (Position == 0 || _resultSaved || _isFinishing)
            return;

        _isFinishing = true;

        try
        {
            RefreshTiming();
            IsRunning = false;
            IsPaused = false;
            _timer.Stop();

            string saveError = "";
            try
            {
                await _processor.SaveResultAsync(
                    DetectedLanguage, TotalCharacters, CorrectChars, Errors,
                    Accuracy, Cpm, Wpm, Math.Max(ElapsedSeconds, 1));
                _resultSaved = true;
                Status = "Result saved";
            }
            catch (Exception ex)
            {
                saveError = ex.Message;
                Status = "Result could not be saved";
            }

            SessionFinished?.Invoke(new TrainingSessionResult(DetectedLanguage, TotalCharacters,
                CorrectChars, Errors, Accuracy, Cpm, Wpm, Math.Max(ElapsedSeconds, 1),
                _resultSaved, saveError));
        }
        finally { _isFinishing = false; }
    }

    private async void OnKeyInput(Keys key, bool shift)
    {
        if (!IsRunning || _disposed || Position >= SourceText.Length)
            return;

        if (IsIgnoredKey(key))
            return;

        var actual = ResolveInputCharacter(key, shift);
        if (!actual.HasValue)
            return;

        var keyName = key.ToString();
        var expected = SourceText[Position];
        if (actual.Value == expected)
        {
            FlashKey(keyName, isError: false);
            LastError = "";
            CorrectChars++;
            Position++;
            AddKeyStat(keyName);
            UpdatePreview();
            RefreshTiming();

            if (Position >= SourceText.Length)
                await FinishAsync();
        }
        else
        {
            FlashKey(keyName, isError: true);
            Errors++;
            LastError = $"Expected '{DisplayChar(expected)}', got '{DisplayChar(actual.Value)}'";
        }
    }

    private static char? ResolveInputCharacter(Keys key, bool shift)
    {
        if (key is Keys.Enter or Keys.Return) return '\n';
        if (key == Keys.Tab) return '\t';
        if (key == Keys.Space) return ' ';
        return KeyTextResolver.ResolveChar(key, shift);
    }

    private void AddKeyStat(string keyName)
    {
        if (!_keyStats.TryGetValue(keyName, out var stat))
        {
            stat = new TrainingKeyStat { KeyName = keyName };
            _keyStats[keyName] = stat;
            KeyStats.Add(stat);
        }

        stat.Count++;
        SessionKeyCounts[keyName] = stat.Count;
        OnPropertyChanged(nameof(SessionKeyCounts));
    }

    private void FlashKey(string keyName, bool isError)
    {
        ActiveKeyName = keyName;
        ActiveKeyIsError = isError;
        _keyFlashTimer.Stop();
        _keyFlashTimer.Start();
    }

    private void RefreshTiming()
    {
        if (IsRunning)
            ElapsedSeconds = Math.Max(0, (int)Math.Round((_pausedElapsed + (DateTime.Now - _startedAt)).TotalSeconds));

        var minutes = Math.Max(ElapsedSeconds / 60.0, 1.0 / 60.0);
        Cpm = Math.Round(CorrectChars / minutes, 1);
        Wpm = Math.Round(CorrectChars / 5.0 / minutes, 1);
    }

    private void ResetProgressOnly()
    {
        _resultSaved = false;
        _keyStats.Clear();
        SessionKeyCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        KeyStats.Clear();
        Position = 0;
        CorrectChars = 0;
        Errors = 0;
        Cpm = 0;
        Wpm = 0;
        ElapsedSeconds = 0;
        LastError = "";
        OnPropertyChanged(nameof(TotalCharacters));
        OnPropertyChanged(nameof(SessionKeyCounts));
        OnPropertyChanged(nameof(RemainingCharacters));
        OnPropertyChanged(nameof(ProgressPercent));
        OnPropertyChanged(nameof(Accuracy));
        OnPropertyChanged(nameof(DetectedLanguage));
        OnPropertyChanged(nameof(CanStart));
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        var safePosition = Math.Clamp(Position, 0, SourceText.Length);
        var pageStart = safePosition == SourceText.Length && safePosition > 0
            ? (safePosition - 1) / PreviewPageLength * PreviewPageLength
            : safePosition / PreviewPageLength * PreviewPageLength;
        var pageEnd = Math.Min(SourceText.Length, pageStart + PreviewPageLength);
        TypedPrefix = DisplayPreview(SourceText.Substring(pageStart, safePosition - pageStart));

        var next = safePosition < SourceText.Length ? SourceText[safePosition] : (char?)null;
        CurrentCharacter = next is { } character && character != ' '
            ? DisplayPreview(character.ToString()) : "";
        CurrentSpace = next == ' ' ? "\u00a0" : "";

        var upcomingStart = Math.Min(SourceText.Length, safePosition + 1);
        UpcomingText = upcomingStart < pageEnd
            ? DisplayPreview(SourceText.Substring(upcomingStart, pageEnd - upcomingStart)) : "";
    }

    private static string DisplayPreview(string text) => text
        .Replace(' ', '\u00a0').Replace('\n', '↵').Replace('\r', '↵').Replace('\t', '⇥');

    private static bool IsIgnoredKey(Keys key) =>
        key is Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey
            or Keys.ControlKey or Keys.LControlKey or Keys.RControlKey
            or Keys.Menu or Keys.LMenu or Keys.RMenu
            or Keys.Capital;

    private static string DisplayChar(char ch) => ch switch
    {
        '\n' => "Enter",
        '\t' => "Tab",
        ' ' => "Space",
        _ => ch.ToString()
    };

    private static string DetectLanguage(string text)
    {
        var cyrillic = text.Count(c => c >= '\u0400' && c <= '\u04FF');
        var latin = text.Count(c => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'));

        if (cyrillic == 0 && latin == 0) return "Other";
        if (cyrillic > 0 && latin > 0) return "Mixed";
        return cyrillic > latin ? "Russian" : "English";
    }

    private void UpdateLayoutInfo(bool force = false)
    {
        var (code, _) = LayoutService.GetCurrent();
        if (!force && code == LayoutCode) return;
        LayoutCode = code;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _keyFlashTimer.Stop();
        _layoutTimer.Stop();
        _liveBus.KeyInput -= OnKeyInput;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
