using System.Windows.Forms;

namespace ClickStat.Infrastructure.Diagnostics;

/// <summary>
/// Disabled input diagnostics shim.
/// Kept so input services can call diagnostics without writing files in normal builds.
/// </summary>
public static class InputLog
{
    public static bool Enabled { get; set; } = false;

    public static void Key(string source, string direction, Keys key)
    {
        _ = source;
        _ = direction;
        _ = key;
    }

    public static void Suppress(string source, string direction, Keys key, string reason)
    {
        _ = source;
        _ = direction;
        _ = key;
        _ = reason;
    }

    public static void Emit(string direction, Keys key)
    {
        _ = direction;
        _ = key;
    }

    public static void Info(string msg)
    {
        _ = msg;
    }

    public static void Clear() { }
}
