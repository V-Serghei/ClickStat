using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using ClickStat.Infrastructure.InputMonitoring;
using ClickStat.Presentation.Services;

namespace ClickStat.Presentation.ViewModels;

public sealed class GamepadsViewModel : INotifyPropertyChanged
{
    private readonly GamepadMonitorService _monitor;
    private string _statusText = LocalizationService.Instance["Gamepads.InitialStatus"];

    public ObservableCollection<GamepadDeviceViewModel> Gamepads { get; } = new();
    public ICommand AddGamepadCommand { get; }

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (_statusText == value)
                return;

            _statusText = value;
            OnPropertyChanged();
        }
    }

    public bool HasGamepads => Gamepads.Count > 0;

    public GamepadsViewModel(GamepadMonitorService monitor)
    {
        _monitor = monitor;
        AddGamepadCommand = new RelayCommand(_ => RefreshDevices());

        _monitor.SnapshotsChanged += OnSnapshotsChanged;
        LocalizationService.Instance.PropertyChanged += OnLocalizationChanged;
    }

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != "Item[]" && e.PropertyName != nameof(LocalizationService.CurrentLanguage))
            return;

        RefreshStatusText();
        foreach (var gamepad in Gamepads)
            gamepad.RefreshLocalization();
    }

    public Task LoadAsync()
    {
        _monitor.Start();
        ApplySnapshots(_monitor.GetSnapshots());
        return Task.CompletedTask;
    }

    private void RefreshDevices()
    {
        _monitor.Start();
        _monitor.ScanNow();
    }

    public void StopMonitoring()
    {
        _monitor.Stop();
    }

    private void OnSnapshotsChanged(object? sender, IReadOnlyList<GamepadSnapshot> snapshots)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            ApplySnapshots(snapshots);
            return;
        }

        dispatcher.InvokeAsync(() => ApplySnapshots(snapshots));
    }

    private void ApplySnapshots(IReadOnlyList<GamepadSnapshot> snapshots)
    {
        foreach (var snapshot in snapshots)
        {
            var existing = Gamepads.FirstOrDefault(x => x.DeviceId == snapshot.DeviceId);
            if (existing == null)
            {
                existing = new GamepadDeviceViewModel(snapshot);
                Gamepads.Add(existing);
            }
            else
            {
                existing.Update(snapshot);
            }
        }

        var knownIds = snapshots.Select(x => x.DeviceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var i = Gamepads.Count - 1; i >= 0; i--)
        {
            if (!knownIds.Contains(Gamepads[i].DeviceId))
                Gamepads.RemoveAt(i);
        }

        RefreshStatusText();

        OnPropertyChanged(nameof(HasGamepads));
    }

    private void RefreshStatusText()
    {
        var connected = Gamepads.Count(x => x.IsConnected);
        StatusText = Gamepads.Count == 0
            ? LocalizationService.Instance["Gamepads.NoneStatus"]
            : string.Format(
                LocalizationService.Instance["Gamepads.FoundStatus"],
                Gamepads.Count,
                connected);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class GamepadDeviceViewModel : INotifyPropertyChanged
{
    private string _displayName = "";
    private string _profileName = "";
    private string _profileDescription = "";
    private bool _isConnected;
    private double _leftX;
    private double _leftY;
    private double _rightX;
    private double _rightY;
    private double _leftTrigger;
    private double _rightTrigger;
    private int _totalButtonPresses;
    private int _totalStickMoves;

    public string DeviceId { get; }
    public ObservableCollection<GamepadButtonViewModel> Buttons { get; } = new();

    private bool _isVisualMode;
    public bool IsVisualMode
    {
        get => _isVisualMode;
        set
        {
            if (Set(ref _isVisualMode, value))
                OnPropertyChanged(nameof(ToggleViewText));
        }
    }

    public ICommand ToggleViewCommand { get; }

    public string DisplayName
    {
        get => _displayName;
        private set => Set(ref _displayName, value);
    }

    public string ProfileName
    {
        get => _profileName;
        private set => Set(ref _profileName, value);
    }

    public string ProfileDescription
    {
        get => _profileDescription;
        private set => Set(ref _profileDescription, value);
    }

    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (Set(ref _isConnected, value))
                OnPropertyChanged(nameof(ConnectionText));
        }
    }

    public string ConnectionText => IsConnected ? "LIVE" : LocalizationService.Instance["Common.Disconnected"];
    public string ToggleViewText => IsVisualMode
        ? LocalizationService.Instance["Gamepads.StatsView"]
        : LocalizationService.Instance["Gamepads.SchemaView"];

    public double LeftX
    {
        get => _leftX;
        private set => SetAxis(ref _leftX, value);
    }

    public double LeftY
    {
        get => _leftY;
        private set => SetAxis(ref _leftY, value);
    }

    public double RightX
    {
        get => _rightX;
        private set => SetAxis(ref _rightX, value);
    }

    public double RightY
    {
        get => _rightY;
        private set => SetAxis(ref _rightY, value);
    }

    public double LeftTrigger
    {
        get => _leftTrigger;
        private set => SetAxis(ref _leftTrigger, value);
    }

    public double RightTrigger
    {
        get => _rightTrigger;
        private set => SetAxis(ref _rightTrigger, value);
    }

    public int TotalButtonPresses
    {
        get => _totalButtonPresses;
        private set => Set(ref _totalButtonPresses, value);
    }

    public int TotalStickMoves
    {
        get => _totalStickMoves;
        private set => Set(ref _totalStickMoves, value);
    }

    public double LeftXBar => Math.Abs(LeftX) * 100;
    public double LeftYBar => Math.Abs(LeftY) * 100;
    public double RightXBar => Math.Abs(RightX) * 100;
    public double RightYBar => Math.Abs(RightY) * 100;
    public double LeftTriggerBar => LeftTrigger * 100;
    public double RightTriggerBar => RightTrigger * 100;

    public string LeftStickText => FormatStick(LeftX, LeftY);
    public string RightStickText => FormatStick(RightX, RightY);
    public string LeftTriggerText => $"{LeftTrigger * 100:0}%";
    public string RightTriggerText => $"{RightTrigger * 100:0}%";

    // Stick indicator positions within a 68×68 canvas (dot is 14×14).
    // Y is negated because XInput positive-Y = stick UP, but canvas Top increases downward.
    public double LeftStickDotX  => 20.0 + LeftX  * 20.0;
    public double LeftStickDotY  => 20.0 - LeftY  * 20.0;
    public double RightStickDotX => 20.0 + RightX * 20.0;
    public double RightStickDotY => 20.0 - RightY * 20.0;

    // Named button lookups for the visual controller layout
    private GamepadButtonViewModel? Btn(string name) =>
        Buttons.FirstOrDefault(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase));

    public GamepadButtonViewModel? BtnA     => Btn("A");
    public GamepadButtonViewModel? BtnB     => Btn("B");
    public GamepadButtonViewModel? BtnX     => Btn("X");
    public GamepadButtonViewModel? BtnY     => Btn("Y");
    public GamepadButtonViewModel? BtnLB    => Btn("LB");
    public GamepadButtonViewModel? BtnRB    => Btn("RB");
    public GamepadButtonViewModel? BtnLT    => Btn("LT");
    public GamepadButtonViewModel? BtnRT    => Btn("RT");
    public GamepadButtonViewModel? BtnBack  => Btn("Back");
    public GamepadButtonViewModel? BtnStart => Btn("Start");
    public GamepadButtonViewModel? BtnLS    => Btn("LS");
    public GamepadButtonViewModel? BtnRS    => Btn("RS");
    public GamepadButtonViewModel? BtnDUp    => Btn("Up");
    public GamepadButtonViewModel? BtnDDown  => Btn("Down");
    public GamepadButtonViewModel? BtnDLeft  => Btn("Left");
    public GamepadButtonViewModel? BtnDRight => Btn("Right");

    public GamepadDeviceViewModel(GamepadSnapshot snapshot)
    {
        DeviceId = snapshot.DeviceId;
        ToggleViewCommand = new RelayCommand(_ => IsVisualMode = !IsVisualMode);
        Update(snapshot);
    }

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(ConnectionText));
        OnPropertyChanged(nameof(ToggleViewText));
        OnPropertyChanged(nameof(ProfileDescription));
    }

    public void Update(GamepadSnapshot snapshot)
    {
        DisplayName = snapshot.DisplayName;
        IsConnected = snapshot.IsConnected;
        ProfileName = snapshot.DeviceType switch
        {
            GamepadDeviceType.Xbox => "Xbox",
            GamepadDeviceType.PlayStation => "PlayStation",
            _ => "Generic"
        };
        ProfileDescription = snapshot.DeviceType switch
        {
            GamepadDeviceType.Xbox => LocalizationService.Instance["Gamepads.XboxProfileDescription"],
            GamepadDeviceType.PlayStation => LocalizationService.Instance["Gamepads.PlayStationProfileDescription"],
            _ => LocalizationService.Instance["Gamepads.GenericProfileDescription"]
        };

        LeftX = snapshot.LeftX;
        LeftY = snapshot.LeftY;
        RightX = snapshot.RightX;
        RightY = snapshot.RightY;
        LeftTrigger = snapshot.LeftTrigger;
        RightTrigger = snapshot.RightTrigger;
        TotalButtonPresses = snapshot.TotalButtonPresses;
        TotalStickMoves = snapshot.TotalStickMoves;

        UpdateButtons(snapshot.Buttons);
    }

    private void UpdateButtons(IReadOnlyList<GamepadButtonState> buttons)
    {
        foreach (var button in buttons)
        {
            var existing = Buttons.FirstOrDefault(x => x.Name == button.Name);
            if (existing == null)
                Buttons.Add(new GamepadButtonViewModel(button));
            else
                existing.Update(button);
        }

        var known = buttons.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var i = Buttons.Count - 1; i >= 0; i--)
        {
            if (!known.Contains(Buttons[i].Name))
                Buttons.RemoveAt(i);
        }

        OnPropertyChanged(string.Empty); // refresh all Btn* computed accessors
    }

    private static string FormatStick(double x, double y) => $"X {FormatAxis(x)} / Y {FormatAxis(y)}";
    private static string FormatAxis(double value) => $"{value * 100:+0;-0;0}%";

    private bool SetAxis(ref double field, double value, [CallerMemberName] string? propertyName = null)
    {
        if (Math.Abs(field - value) < 0.001)
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        OnAxisPropertiesChanged();
        return true;
    }

    private void OnAxisPropertiesChanged()
    {
        OnPropertyChanged(nameof(LeftXBar));
        OnPropertyChanged(nameof(LeftYBar));
        OnPropertyChanged(nameof(RightXBar));
        OnPropertyChanged(nameof(RightYBar));
        OnPropertyChanged(nameof(LeftTriggerBar));
        OnPropertyChanged(nameof(RightTriggerBar));
        OnPropertyChanged(nameof(LeftStickText));
        OnPropertyChanged(nameof(RightStickText));
        OnPropertyChanged(nameof(LeftTriggerText));
        OnPropertyChanged(nameof(RightTriggerText));
        OnPropertyChanged(nameof(LeftStickDotX));
        OnPropertyChanged(nameof(LeftStickDotY));
        OnPropertyChanged(nameof(RightStickDotX));
        OnPropertyChanged(nameof(RightStickDotY));
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class GamepadButtonViewModel : INotifyPropertyChanged
{
    private string _name = "";
    private bool _isPressed;
    private int _count;

    public string Name
    {
        get => _name;
        private set => Set(ref _name, value);
    }

    public bool IsPressed
    {
        get => _isPressed;
        private set => Set(ref _isPressed, value);
    }

    public int Count
    {
        get => _count;
        private set => Set(ref _count, value);
    }

    public GamepadButtonViewModel(GamepadButtonState state)
    {
        Update(state);
    }

    public void Update(GamepadButtonState state)
    {
        Name = state.Name;
        IsPressed = state.IsPressed;
        Count = state.Count;
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
