using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;

namespace ClickStat.Infrastructure.InputMonitoring;

public sealed class GamepadMonitorService : IDisposable
{
    private const int PollIntervalMs = 33;
    private const int SaveIntervalMs = 5000;
    private const uint ErrorSuccess = 0;
    private const uint JoyReturnAll = 0x000000FF;
    private const double StickDeadZone = 0.08;
    private const double StickMoveThreshold = 0.04;
    private const double TriggerPressedThreshold = 0.35;

    private readonly object _gate = new();
    private readonly Dictionary<string, GamepadRuntimeState> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string DeviceId, string ButtonName), GamepadButtonDelta> _buttonBuffer = new();
    private readonly Dictionary<string, GamepadMoveDelta> _moveBuffer = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _dbPath;
    private Timer? _timer;
    private Timer? _saveTimer;

    public event EventHandler<IReadOnlyList<GamepadSnapshot>>? SnapshotsChanged;

    public bool IsRunning { get; private set; }

    public GamepadMonitorService()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var folder = Path.Combine(documents, "KeyClick");
        Directory.CreateDirectory(folder);
        _dbPath = Path.Combine(folder, "key_statistics.db");
        try
        {
            EnsureSchema();
        }
        catch
        {
            // The app can still show live gamepad data; persistence will retry on later flushes.
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (IsRunning)
                return;

            IsRunning = true;
            ScanNowCore();
            _timer = new Timer(_ => PollSafely(), null, 0, PollIntervalMs);
            _saveTimer = new Timer(_ => FlushSafely(), null, SaveIntervalMs, SaveIntervalMs);
        }
    }

    public void Stop()
    {
        Timer? pollTimer;
        Timer? saveTimer;

        lock (_gate)
        {
            IsRunning = false;
            pollTimer = _timer;
            saveTimer = _saveTimer;
            _timer = null;
            _saveTimer = null;
        }

        pollTimer?.Dispose();
        saveTimer?.Dispose();
        FlushSafely();
    }

    public void ScanNow()
    {
        IReadOnlyList<GamepadSnapshot> snapshots;
        lock (_gate)
        {
            ScanNowCore();
            PollKnownDevicesCore();
            snapshots = BuildSnapshotsLocked();
        }

        SnapshotsChanged?.Invoke(this, snapshots);
    }

    public IReadOnlyList<GamepadSnapshot> GetSnapshots()
    {
        lock (_gate)
        {
            return BuildSnapshotsLocked();
        }
    }

    public void Dispose() => Stop();

    private void PollSafely()
    {
        IReadOnlyList<GamepadSnapshot> snapshots;
        try
        {
            lock (_gate)
            {
                ScanNowCore();
                PollKnownDevicesCore();
                snapshots = BuildSnapshotsLocked();
            }
        }
        catch
        {
            return;
        }

        SnapshotsChanged?.Invoke(this, snapshots);
    }

    private void ScanNowCore()
    {
        foreach (var device in _devices.Values)
            device.SeenOnLastScan = false;

        uint xInputCount = 0;
        for (uint slot = 0; slot < 4; slot++)
        {
            if (!TryReadXInput(slot, out _))
                continue;

            xInputCount++;
            var device = GetOrCreate(
                $"xinput:{slot}",
                $"Xbox Controller {slot + 1}",
                GamepadDeviceType.Xbox,
                GamepadSource.XInput);

            device.XInputSlot = slot;
            device.SeenOnLastScan = true;
            device.IsConnected = true;
        }

        var joystickCount = JoyGetNumDevs();
        for (uint joystickId = 0; joystickId < joystickCount; joystickId++)
        {
            if (!TryGetJoyCaps(joystickId, out var caps) || !TryReadJoystick(joystickId, out _))
                continue;

            var name = string.IsNullOrWhiteSpace(caps.szPname)
                ? $"Joystick {joystickId + 1}"
                : caps.szPname.Trim();

            var type = InferDeviceType(name);
            if (joystickId < xInputCount || (xInputCount > 0 && type == GamepadDeviceType.Xbox))
                continue;

            var device = GetOrCreate(
                $"winmm:{joystickId}",
                name,
                type,
                GamepadSource.WinMm);

            device.JoystickId = joystickId;
            device.JoyCaps = caps;
            device.SeenOnLastScan = true;
            device.IsConnected = true;
        }

        foreach (var device in _devices.Values.Where(d => !d.SeenOnLastScan))
        {
            device.IsConnected = false;
            device.PreviousPressed.Clear();
        }
    }

    private GamepadRuntimeState GetOrCreate(
        string id,
        string displayName,
        GamepadDeviceType type,
        GamepadSource source)
    {
        if (_devices.TryGetValue(id, out var existing))
        {
            existing.DisplayName = displayName;
            existing.DeviceType = type;
            return existing;
        }

        var created = new GamepadRuntimeState
        {
            DeviceId = id,
            DisplayName = displayName,
            DeviceType = type,
            Source = source
        };

        LoadSavedState(created);
        _devices[id] = created;
        return created;
    }

    private void PollKnownDevicesCore()
    {
        foreach (var device in _devices.Values)
            PollDevice(device);
    }

    private void PollDevice(GamepadRuntimeState device)
    {
        switch (device.Source)
        {
            case GamepadSource.XInput:
                PollXInputDevice(device);
                break;
            case GamepadSource.WinMm:
                PollWinMmDevice(device);
                break;
        }
    }

    private void PollXInputDevice(GamepadRuntimeState device)
    {
        if (!TryReadXInput(device.XInputSlot, out var state))
        {
            device.IsConnected = false;
            device.PreviousPressed.Clear();
            return;
        }

        var gamepad = state.Gamepad;
        var buttons = new Dictionary<string, bool>
        {
            ["A"] = HasButton(gamepad.wButtons, XInputButtons.A),
            ["B"] = HasButton(gamepad.wButtons, XInputButtons.B),
            ["X"] = HasButton(gamepad.wButtons, XInputButtons.X),
            ["Y"] = HasButton(gamepad.wButtons, XInputButtons.Y),
            ["LB"] = HasButton(gamepad.wButtons, XInputButtons.LeftShoulder),
            ["RB"] = HasButton(gamepad.wButtons, XInputButtons.RightShoulder),
            ["LT"] = NormalizeTrigger(gamepad.bLeftTrigger) >= TriggerPressedThreshold,
            ["RT"] = NormalizeTrigger(gamepad.bRightTrigger) >= TriggerPressedThreshold,
            ["Back"] = HasButton(gamepad.wButtons, XInputButtons.Back),
            ["Start"] = HasButton(gamepad.wButtons, XInputButtons.Start),
            ["LS"] = HasButton(gamepad.wButtons, XInputButtons.LeftThumb),
            ["RS"] = HasButton(gamepad.wButtons, XInputButtons.RightThumb),
            ["Up"] = HasButton(gamepad.wButtons, XInputButtons.DPadUp),
            ["Down"] = HasButton(gamepad.wButtons, XInputButtons.DPadDown),
            ["Left"] = HasButton(gamepad.wButtons, XInputButtons.DPadLeft),
            ["Right"] = HasButton(gamepad.wButtons, XInputButtons.DPadRight)
        };

        ApplyState(
            device,
            isConnected: true,
            leftX: NormalizeThumb(gamepad.sThumbLX),
            leftY: NormalizeThumb(gamepad.sThumbLY),
            rightX: NormalizeThumb(gamepad.sThumbRX),
            rightY: NormalizeThumb(gamepad.sThumbRY),
            leftTrigger: NormalizeTrigger(gamepad.bLeftTrigger),
            rightTrigger: NormalizeTrigger(gamepad.bRightTrigger),
            buttons);
    }

    private void PollWinMmDevice(GamepadRuntimeState device)
    {
        if (!TryReadJoystick(device.JoystickId, out var info))
        {
            device.IsConnected = false;
            device.PreviousPressed.Clear();
            return;
        }

        var caps = device.JoyCaps;
        var names = GetButtonNames(device.DeviceType);
        var reportedButtons = caps.wNumButtons == 0 ? 8u : caps.wNumButtons;
        var maxButtons = (int)Math.Min(reportedButtons, 32u);
        var buttons = new Dictionary<string, bool>();

        for (var i = 0; i < maxButtons; i++)
        {
            var name = i < names.Length ? names[i] : $"B{i + 1}";
            buttons[name] = (info.dwButtons & (1u << i)) != 0;
        }

        var leftX = NormalizeJoystickAxis(info.dwXpos, caps.wXmin, caps.wXmax);
        var leftY = -NormalizeJoystickAxis(info.dwYpos, caps.wYmin, caps.wYmax);
        var rightX = NormalizeJoystickAxis(info.dwZpos, caps.wZmin, caps.wZmax);
        var rightY = -NormalizeJoystickAxis(info.dwRpos, caps.wRmin, caps.wRmax);
        var leftTrigger = NormalizePositiveAxis(info.dwUpos, caps.wUmin, caps.wUmax);
        var rightTrigger = NormalizePositiveAxis(info.dwVpos, caps.wVmin, caps.wVmax);

        ApplyState(device, true, leftX, leftY, rightX, rightY, leftTrigger, rightTrigger, buttons);
    }

    private void ApplyState(
        GamepadRuntimeState device,
        bool isConnected,
        double leftX,
        double leftY,
        double rightX,
        double rightY,
        double leftTrigger,
        double rightTrigger,
        IReadOnlyDictionary<string, bool> buttons)
    {
        foreach (var pressed in buttons.Where(x => x.Value && !device.PreviousPressed.Contains(x.Key)))
        {
            device.ButtonCounts.TryGetValue(pressed.Key, out var count);
            device.ButtonCounts[pressed.Key] = count + 1;
            device.TotalButtonPresses++;
            BufferButtonDelta(device, pressed.Key);
        }

        device.PreviousPressed.Clear();
        foreach (var pressed in buttons.Where(x => x.Value))
            device.PreviousPressed.Add(pressed.Key);

        if (HasMeaningfulStickDelta(device, leftX, leftY, rightX, rightY, leftTrigger, rightTrigger))
        {
            device.TotalStickMoves++;
            BufferMoveDelta(device);
        }

        device.IsConnected = isConnected;
        device.LeftX = leftX;
        device.LeftY = leftY;
        device.RightX = rightX;
        device.RightY = rightY;
        device.LeftTrigger = leftTrigger;
        device.RightTrigger = rightTrigger;
        device.ButtonOrder = buttons.Keys.ToArray();
    }

    private static bool HasMeaningfulStickDelta(
        GamepadRuntimeState device,
        double leftX,
        double leftY,
        double rightX,
        double rightY,
        double leftTrigger,
        double rightTrigger)
    {
        var hasMotion =
            Math.Abs(leftX) > StickDeadZone ||
            Math.Abs(leftY) > StickDeadZone ||
            Math.Abs(rightX) > StickDeadZone ||
            Math.Abs(rightY) > StickDeadZone ||
            leftTrigger > TriggerPressedThreshold ||
            rightTrigger > TriggerPressedThreshold;

        if (!hasMotion)
            return false;

        return Math.Abs(device.LeftX - leftX) > StickMoveThreshold ||
               Math.Abs(device.LeftY - leftY) > StickMoveThreshold ||
               Math.Abs(device.RightX - rightX) > StickMoveThreshold ||
               Math.Abs(device.RightY - rightY) > StickMoveThreshold ||
               Math.Abs(device.LeftTrigger - leftTrigger) > StickMoveThreshold ||
               Math.Abs(device.RightTrigger - rightTrigger) > StickMoveThreshold;
    }

    private IReadOnlyList<GamepadSnapshot> BuildSnapshotsLocked()
    {
        return _devices.Values
            .OrderByDescending(d => d.IsConnected)
            .ThenBy(d => d.DeviceType)
            .ThenBy(d => d.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .Select(d =>
            {
                var buttonOrder = d.ButtonOrder.Length > 0
                    ? d.ButtonOrder
                    : GetButtonNames(d.DeviceType);

                var buttons = buttonOrder
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(name =>
                    {
                        d.ButtonCounts.TryGetValue(name, out var count);
                        return new GamepadButtonState(name, d.PreviousPressed.Contains(name), count);
                    })
                    .ToArray();

                return new GamepadSnapshot(
                    d.DeviceId,
                    d.DisplayName,
                    d.DeviceType,
                    d.IsConnected,
                    d.LeftX,
                    d.LeftY,
                    d.RightX,
                    d.RightY,
                    d.LeftTrigger,
                    d.RightTrigger,
                    buttons,
                    d.TotalButtonPresses,
                    d.TotalStickMoves);
            })
            .ToArray();
    }

    private void BufferButtonDelta(GamepadRuntimeState device, string buttonName)
    {
        var key = (device.DeviceId, buttonName);
        if (_buttonBuffer.TryGetValue(key, out var existing))
        {
            _buttonBuffer[key] = existing with
            {
                DisplayName = device.DisplayName,
                DeviceType = device.DeviceType,
                Count = existing.Count + 1
            };
            return;
        }

        _buttonBuffer[key] = new GamepadButtonDelta(device.DisplayName, device.DeviceType, 1);
    }

    private void BufferMoveDelta(GamepadRuntimeState device)
    {
        if (_moveBuffer.TryGetValue(device.DeviceId, out var existing))
        {
            _moveBuffer[device.DeviceId] = existing with
            {
                DisplayName = device.DisplayName,
                DeviceType = device.DeviceType,
                Count = existing.Count + 1
            };
            return;
        }

        _moveBuffer[device.DeviceId] = new GamepadMoveDelta(device.DisplayName, device.DeviceType, 1);
    }

    private void EnsureSchema()
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();

        using var buttonCommand = connection.CreateCommand();
        buttonCommand.CommandText = @"
            CREATE TABLE IF NOT EXISTS GamepadButtonStatistics (
                DeviceId   TEXT NOT NULL,
                ButtonName TEXT NOT NULL,
                DisplayName TEXT NOT NULL,
                DeviceType INTEGER NOT NULL,
                Count      INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (DeviceId, ButtonName)
            )";
        buttonCommand.ExecuteNonQuery();

        using var deviceCommand = connection.CreateCommand();
        deviceCommand.CommandText = @"
            CREATE TABLE IF NOT EXISTS GamepadDeviceStatistics (
                DeviceId        TEXT PRIMARY KEY,
                DisplayName     TEXT NOT NULL,
                DeviceType      INTEGER NOT NULL,
                TotalStickMoves INTEGER NOT NULL DEFAULT 0
            )";
        deviceCommand.ExecuteNonQuery();
    }

    private void LoadSavedState(GamepadRuntimeState device)
    {
        try
        {
            EnsureSchema();
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            connection.Open();

            using (var buttonCommand = connection.CreateCommand())
            {
                buttonCommand.CommandText = @"
                    SELECT ButtonName, Count
                    FROM GamepadButtonStatistics
                    WHERE DeviceId = $deviceId";
                buttonCommand.Parameters.AddWithValue("$deviceId", device.DeviceId);

                using var reader = buttonCommand.ExecuteReader();
                while (reader.Read())
                {
                    var buttonName = reader.GetString(0);
                    var count = reader.GetInt32(1);
                    device.ButtonCounts[buttonName] = count;
                    device.TotalButtonPresses += count;
                }
            }

            using (var moveCommand = connection.CreateCommand())
            {
                moveCommand.CommandText = @"
                    SELECT TotalStickMoves
                    FROM GamepadDeviceStatistics
                    WHERE DeviceId = $deviceId";
                moveCommand.Parameters.AddWithValue("$deviceId", device.DeviceId);

                var value = moveCommand.ExecuteScalar();
                if (value != null && value != DBNull.Value)
                    device.TotalStickMoves = Convert.ToInt32(value);
            }
        }
        catch
        {
            // Gamepad stats are additive; if an old DB is busy, keep live tracking and try saving later.
        }
    }

    private void FlushSafely()
    {
        Dictionary<(string DeviceId, string ButtonName), GamepadButtonDelta> buttonSnapshot;
        Dictionary<string, GamepadMoveDelta> moveSnapshot;

        lock (_gate)
        {
            if (_buttonBuffer.Count == 0 && _moveBuffer.Count == 0)
                return;

            buttonSnapshot = new Dictionary<(string DeviceId, string ButtonName), GamepadButtonDelta>(_buttonBuffer);
            moveSnapshot = new Dictionary<string, GamepadMoveDelta>(_moveBuffer, StringComparer.OrdinalIgnoreCase);
            _buttonBuffer.Clear();
            _moveBuffer.Clear();
        }

        try
        {
            EnsureSchema();
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            connection.Open();
            using var transaction = connection.BeginTransaction();

            foreach (var (key, delta) in buttonSnapshot)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = @"
                    INSERT INTO GamepadButtonStatistics (DeviceId, ButtonName, DisplayName, DeviceType, Count)
                    VALUES ($deviceId, $buttonName, $displayName, $deviceType, $count)
                    ON CONFLICT(DeviceId, ButtonName) DO UPDATE SET
                        DisplayName = excluded.DisplayName,
                        DeviceType = excluded.DeviceType,
                        Count = GamepadButtonStatistics.Count + excluded.Count";
                command.Parameters.AddWithValue("$deviceId", key.DeviceId);
                command.Parameters.AddWithValue("$buttonName", key.ButtonName);
                command.Parameters.AddWithValue("$displayName", delta.DisplayName);
                command.Parameters.AddWithValue("$deviceType", (int)delta.DeviceType);
                command.Parameters.AddWithValue("$count", delta.Count);
                command.ExecuteNonQuery();
            }

            foreach (var (deviceId, delta) in moveSnapshot)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = @"
                    INSERT INTO GamepadDeviceStatistics (DeviceId, DisplayName, DeviceType, TotalStickMoves)
                    VALUES ($deviceId, $displayName, $deviceType, $count)
                    ON CONFLICT(DeviceId) DO UPDATE SET
                        DisplayName = excluded.DisplayName,
                        DeviceType = excluded.DeviceType,
                        TotalStickMoves = GamepadDeviceStatistics.TotalStickMoves + excluded.TotalStickMoves";
                command.Parameters.AddWithValue("$deviceId", deviceId);
                command.Parameters.AddWithValue("$displayName", delta.DisplayName);
                command.Parameters.AddWithValue("$deviceType", (int)delta.DeviceType);
                command.Parameters.AddWithValue("$count", delta.Count);
                command.ExecuteNonQuery();
            }

            transaction.Commit();
        }
        catch
        {
            lock (_gate)
            {
                foreach (var (key, delta) in buttonSnapshot)
                {
                    if (_buttonBuffer.TryGetValue(key, out var existing))
                        _buttonBuffer[key] = existing with { Count = existing.Count + delta.Count };
                    else
                        _buttonBuffer[key] = delta;
                }

                foreach (var (deviceId, delta) in moveSnapshot)
                {
                    if (_moveBuffer.TryGetValue(deviceId, out var existing))
                        _moveBuffer[deviceId] = existing with { Count = existing.Count + delta.Count };
                    else
                        _moveBuffer[deviceId] = delta;
                }
            }
        }
    }

    private static GamepadDeviceType InferDeviceType(string name)
    {
        var normalized = name.ToLowerInvariant();
        if (normalized.Contains("xbox") || normalized.Contains("xinput"))
            return GamepadDeviceType.Xbox;

        if (normalized.Contains("playstation") ||
            normalized.Contains("dualsense") ||
            normalized.Contains("dualshock") ||
            normalized.Contains("wireless controller"))
            return GamepadDeviceType.PlayStation;

        return GamepadDeviceType.Generic;
    }

    private static string[] GetButtonNames(GamepadDeviceType type) => type switch
    {
        GamepadDeviceType.Xbox => new[]
        {
            "A", "B", "X", "Y", "LB", "RB", "LT", "RT",
            "Back", "Start", "LS", "RS", "Up", "Down", "Left", "Right"
        },
        GamepadDeviceType.PlayStation => new[]
        {
            "Square", "Cross", "Circle", "Triangle", "L1", "R1", "L2", "R2",
            "Share", "Options", "L3", "R3", "PS", "Touch", "Up", "Down", "Left", "Right"
        },
        _ => Enumerable.Range(1, 16).Select(i => $"B{i}").ToArray()
    };

    private static bool HasButton(ushort buttons, ushort mask) => (buttons & mask) != 0;

    private static double NormalizeThumb(short value)
    {
        var normalized = value >= 0 ? value / 32767.0 : value / 32768.0;
        return Math.Abs(normalized) < StickDeadZone ? 0 : Math.Clamp(normalized, -1, 1);
    }

    private static double NormalizeTrigger(byte value) => Math.Clamp(value / 255.0, 0, 1);

    private static double NormalizeJoystickAxis(uint value, uint min, uint max)
    {
        if (max <= min)
            return 0;

        var normalized = ((double)value - min) / (max - min) * 2 - 1;
        normalized = Math.Clamp(normalized, -1, 1);
        return Math.Abs(normalized) < StickDeadZone ? 0 : normalized;
    }

    private static double NormalizePositiveAxis(uint value, uint min, uint max)
    {
        if (max <= min)
            return 0;

        return Math.Clamp(((double)value - min) / (max - min), 0, 1);
    }

    private static bool TryReadXInput(uint slot, out XInputState state)
    {
        state = default;
        try
        {
            if (XInputGetState14(slot, out state) == ErrorSuccess)
                return true;
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }

        try
        {
            return XInputGetState910(slot, out state) == ErrorSuccess;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    private static bool TryGetJoyCaps(uint joystickId, out JoyCaps caps)
    {
        caps = default;
        return JoyGetDevCaps(joystickId, ref caps, (uint)Marshal.SizeOf<JoyCaps>()) == ErrorSuccess;
    }

    private static bool TryReadJoystick(uint joystickId, out JoyInfoEx info)
    {
        info = new JoyInfoEx
        {
            dwSize = (uint)Marshal.SizeOf<JoyInfoEx>(),
            dwFlags = JoyReturnAll
        };

        return JoyGetPosEx(joystickId, ref info) == ErrorSuccess;
    }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern uint XInputGetState14(uint dwUserIndex, out XInputState pState);

    [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
    private static extern uint XInputGetState910(uint dwUserIndex, out XInputState pState);

    [DllImport("winmm.dll", EntryPoint = "joyGetNumDevs")]
    private static extern uint JoyGetNumDevs();

    [DllImport("winmm.dll", EntryPoint = "joyGetDevCapsW", CharSet = CharSet.Unicode)]
    private static extern uint JoyGetDevCaps(uint uJoyID, ref JoyCaps pjc, uint cbjc);

    [DllImport("winmm.dll", EntryPoint = "joyGetPosEx")]
    private static extern uint JoyGetPosEx(uint uJoyID, ref JoyInfoEx pji);

    private sealed class GamepadRuntimeState
    {
        public required string DeviceId { get; init; }
        public required string DisplayName { get; set; }
        public required GamepadDeviceType DeviceType { get; set; }
        public required GamepadSource Source { get; init; }
        public bool SeenOnLastScan { get; set; }
        public bool IsConnected { get; set; }
        public uint XInputSlot { get; set; }
        public uint JoystickId { get; set; }
        public JoyCaps JoyCaps { get; set; }
        public double LeftX { get; set; }
        public double LeftY { get; set; }
        public double RightX { get; set; }
        public double RightY { get; set; }
        public double LeftTrigger { get; set; }
        public double RightTrigger { get; set; }
        public int TotalButtonPresses { get; set; }
        public int TotalStickMoves { get; set; }
        public string[] ButtonOrder { get; set; } = Array.Empty<string>();
        public HashSet<string> PreviousPressed { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> ButtonCounts { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record GamepadButtonDelta(string DisplayName, GamepadDeviceType DeviceType, int Count);
    private sealed record GamepadMoveDelta(string DisplayName, GamepadDeviceType DeviceType, int Count);

    private enum GamepadSource
    {
        XInput,
        WinMm
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState
    {
        public uint dwPacketNumber;
        public XInputGamepad Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputGamepad
    {
        public ushort wButtons;
        public byte bLeftTrigger;
        public byte bRightTrigger;
        public short sThumbLX;
        public short sThumbLY;
        public short sThumbRX;
        public short sThumbRY;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct JoyCaps
    {
        public ushort wMid;
        public ushort wPid;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szPname;
        public uint wXmin;
        public uint wXmax;
        public uint wYmin;
        public uint wYmax;
        public uint wZmin;
        public uint wZmax;
        public uint wNumButtons;
        public uint wPeriodMin;
        public uint wPeriodMax;
        public uint wRmin;
        public uint wRmax;
        public uint wUmin;
        public uint wUmax;
        public uint wVmin;
        public uint wVmax;
        public uint wCaps;
        public uint wMaxAxes;
        public uint wNumAxes;
        public uint wMaxButtons;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szRegKey;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szOEMVxD;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JoyInfoEx
    {
        public uint dwSize;
        public uint dwFlags;
        public uint dwXpos;
        public uint dwYpos;
        public uint dwZpos;
        public uint dwRpos;
        public uint dwUpos;
        public uint dwVpos;
        public uint dwButtons;
        public uint dwButtonNumber;
        public uint dwPOV;
        public uint dwReserved1;
        public uint dwReserved2;
    }

    private static class XInputButtons
    {
        public const ushort DPadUp = 0x0001;
        public const ushort DPadDown = 0x0002;
        public const ushort DPadLeft = 0x0004;
        public const ushort DPadRight = 0x0008;
        public const ushort Start = 0x0010;
        public const ushort Back = 0x0020;
        public const ushort LeftThumb = 0x0040;
        public const ushort RightThumb = 0x0080;
        public const ushort LeftShoulder = 0x0100;
        public const ushort RightShoulder = 0x0200;
        public const ushort A = 0x1000;
        public const ushort B = 0x2000;
        public const ushort X = 0x4000;
        public const ushort Y = 0x8000;
    }
}
