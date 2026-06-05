using System;
using System.IO;
using System.Windows.Forms;

namespace ClickStat.Infrastructure.Diagnostics;

/// <summary>
/// Lightweight debug logger for input events.
/// Writes to Documents/KeyClick/input_debug.log
/// Toggle with InputLog.Enabled = true/false.
/// </summary>
public static class InputLog
{
    public static bool Enabled { get; set; } = true;

    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "KeyClick", "input_debug.log");

    private static readonly object _lock = new();
    private static int _lineCount;
    private const int MaxLines = 5000; // rotate after 5k lines

    public static void Key(string source, string direction, Keys key)
    {
        if (!Enabled) return;
        Write($"KEY  [{source,4}] {direction,4} {key,-20} ({(int)key})");
    }

    public static void Suppress(string source, string direction, Keys key, string reason)
    {
        if (!Enabled) return;
        Write($"SUPP [{source,4}] {direction,4} {key,-20} ({(int)key}) — {reason}");
    }

    public static void Emit(string direction, Keys key)
    {
        if (!Enabled) return;
        Write($"EMIT       {direction,4} {key,-20} → MainViewModel");
    }

    public static void Info(string msg)
    {
        if (!Enabled) return;
        Write($"INFO {msg}");
    }

    private static void Write(string msg)
    {
        try
        {
            lock (_lock)
            {
                _lineCount++;
                if (_lineCount > MaxLines)
                {
                    File.WriteAllText(LogPath, $"{Ts()} [log rotated]\n");
                    _lineCount = 1;
                }
                File.AppendAllText(LogPath, $"{Ts()} {msg}\n");
            }
        }
        catch { /* never crash the app due to logging */ }
    }

    private static string Ts() => DateTime.Now.ToString("HH:mm:ss.fff");

    public static void Clear()
    {
        lock (_lock)
        {
            File.WriteAllText(LogPath, $"{Ts()} [log cleared]\n");
            _lineCount = 0;
        }
    }
}
