using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Threading;
using ClickStat.Infrastructure.Data;
using ClickStat.Infrastructure.Services;
using ClickStat.Presentation.Model;
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
    private const int PreviewRadius = 42;
    private readonly LiveEventBus _liveBus;
    private readonly TypingTrainingProcessor _processor;
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _keyFlashTimer;
    private readonly DispatcherTimer _layoutTimer;
    private readonly Dictionary<string, TrainingKeyStat> _keyStats = new(StringComparer.OrdinalIgnoreCase);
    private readonly TypingTrainingSession _session = new();
    private DateTime _startedAt;
    private TimeSpan _pausedElapsed;
    private bool _disposed;
    private bool _resultSaved;

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

    public string SourceText
    {
        get => _session.SourceText;
        set
        {
            var next = value ?? "";
            if (_session.SourceText == next) return;
            _session.SourceText = next;
            OnPropertyChanged();
            NotifySessionPropertiesChanged();
            OnPropertyChanged(nameof(CanStart));
            if (Position == 0)
                UpdatePreview();
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

    public int Position => _session.Position;
    public int CorrectChars => _session.CorrectChars;
    public int Errors => _session.Errors;

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

    public int TotalCharacters => _session.TotalCharacters;
    public int RemainingCharacters => _session.RemainingCharacters;
    public double ProgressPercent => _session.ProgressPercent;
    public double Accuracy => _session.Accuracy;
    public bool CanStart => !IsRunning && !string.IsNullOrEmpty(SourceText);
    public bool IsTextLocked => IsRunning || IsPaused || _resultSaved;
    public string DetectedLanguage => _session.DetectedLanguage;
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
        Status = Loc("Training.StartedStatus");
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
            Status = Loc("Training.PausedStatus");
        }
        else
        {
            _startedAt = DateTime.Now;
            IsRunning = true;
            IsPaused = false;
            _timer.Start();
            Status = Loc("Training.ResumedStatus");
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
        if (Position == 0)
            return;

        if (_resultSaved)
            return;

        RefreshTiming();
        IsRunning = false;
        IsPaused = false;
        _timer.Stop();

        await _processor.SaveResultAsync(
            DetectedLanguage,
            TotalCharacters,
            CorrectChars,
            Errors,
            Accuracy,
            Cpm,
            Wpm,
            Math.Max(ElapsedSeconds, 1));

        Status = Loc("Training.ResultSavedStatus");
        _resultSaved = true;
        OnPropertyChanged(nameof(IsTextLocked));
        OnPropertyChanged(nameof(CanStart));
    }

    private async void OnKeyInput(Keys key, bool shift)
    {
        if (!IsRunning || _disposed || _session.IsComplete)
            return;

        if (IsIgnoredKey(key))
            return;

        var actual = ResolveInputCharacter(key, shift);
        if (!actual.HasValue)
            return;

        var keyName = key.ToString();
        AddKeyStat(keyName);
        var result = _session.Accept(actual.Value, out var expected);
        if (result == TypingTrainingInputResult.Correct)
        {
            FlashKey(keyName, isError: false);
            LastError = "";
            NotifySessionPropertiesChanged();
            UpdatePreview();
            RefreshTiming();

            if (_session.IsComplete)
                await FinishAsync();
        }
        else
        {
            FlashKey(keyName, isError: true);
            NotifySessionPropertiesChanged();
            LastError = string.Format(
                Loc("Training.ExpectedGot"),
                TypingTrainingSession.DisplayChar(expected),
                TypingTrainingSession.DisplayChar(actual.Value));
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
        _keyStats.Clear();
        SessionKeyCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        KeyStats.Clear();
        _resultSaved = false;
        OnPropertyChanged(nameof(IsTextLocked));
        _session.ResetProgress();
        Cpm = 0;
        Wpm = 0;
        ElapsedSeconds = 0;
        LastError = "";
        NotifySessionPropertiesChanged();
        OnPropertyChanged(nameof(SessionKeyCounts));
        OnPropertyChanged(nameof(CanStart));
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        var preview = _session.GetPreview(PreviewRadius);
        TypedPrefix = preview.TypedPrefix;
        CurrentCharacter = preview.CurrentCharacter;
        UpcomingText = preview.UpcomingText;
    }

    private static bool IsIgnoredKey(Keys key) =>
        key is Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey
            or Keys.ControlKey or Keys.LControlKey or Keys.RControlKey
            or Keys.Menu or Keys.LMenu or Keys.RMenu
            or Keys.Capital;

    private static string Loc(string key) => LocalizationService.Instance[key];

    private void NotifySessionPropertiesChanged()
    {
        OnPropertyChanged(nameof(Position));
        OnPropertyChanged(nameof(CorrectChars));
        OnPropertyChanged(nameof(Errors));
        OnPropertyChanged(nameof(TotalCharacters));
        OnPropertyChanged(nameof(RemainingCharacters));
        OnPropertyChanged(nameof(ProgressPercent));
        OnPropertyChanged(nameof(Accuracy));
        OnPropertyChanged(nameof(DetectedLanguage));
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
