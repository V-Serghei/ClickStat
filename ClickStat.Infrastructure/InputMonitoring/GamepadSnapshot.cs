namespace ClickStat.Infrastructure.InputMonitoring;

public sealed record GamepadSnapshot(
    string DeviceId,
    string DisplayName,
    GamepadDeviceType DeviceType,
    bool IsConnected,
    double LeftX,
    double LeftY,
    double RightX,
    double RightY,
    double LeftTrigger,
    double RightTrigger,
    IReadOnlyList<GamepadButtonState> Buttons,
    int TotalButtonPresses,
    int TotalStickMoves);
