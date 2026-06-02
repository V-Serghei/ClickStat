using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;

namespace ClickStat.Presentation.Converters;

public class KeyNameToLabelConverter : IValueConverter
{
    private static readonly Dictionary<string, string> Labels = new()
    {
        // Special
        ["Escape"]      = "Esc",
        ["Back"]        = "⌫",
        ["Tab"]         = "Tab",
        ["Capital"]     = "Caps",
        ["Enter"]       = "↵",
        ["Space"]       = "Space",
        ["LShiftKey"]   = "Shift",
        ["RShiftKey"]   = "Shift",
        ["LControlKey"] = "Ctrl",
        ["RControlKey"] = "Ctrl",
        ["LWin"]        = "Win",
        ["RWin"]        = "Win",
        ["LMenu"]       = "Alt",
        ["RMenu"]       = "Alt",

        // Symbol row
        ["Oemtilde"]    = "` ~",
        ["D1"] = "1",  ["D2"] = "2",  ["D3"] = "3",  ["D4"] = "4",  ["D5"] = "5",
        ["D6"] = "6",  ["D7"] = "7",  ["D8"] = "8",  ["D9"] = "9",  ["D0"] = "0",
        ["OemMinus"]    = "- _",
        ["Oemplus"]     = "= +",
        ["Oem4"]        = "[ {",
        ["Oem6"]        = "] }",
        ["OemPipe"]     = "\\ |",
        ["OemSemicolon"]= "; :",
        ["OemQuotes"]   = "' \"",
        ["Oemcomma"]    = ", <",
        ["OemPeriod"]   = ". >",
        ["Oem2"]        = "/ ?",

        // Navigation
        ["Insert"]      = "Ins",
        ["Delete"]      = "Del",
        ["Next"]        = "PgDn",
        ["PageUp"]      = "PgUp",
        ["PrintScreen"] = "PrtSc",
        ["LaunchApplication2"] = "ScrLk",
        ["MediaStop"]   = "Pause",
        ["Scroll"]      = "ScrLk",
        ["Pause"]       = "Pause",

        // Arrows
        ["Up"]    = "↑",
        ["Down"]  = "↓",
        ["Left"]  = "←",
        ["Right"] = "→",

        // Numpad
        ["NumLock"]  = "Num",
        ["Clear"]    = "Clr",
        ["Divide"]   = "/",
        ["Multiply"] = "*",
        ["Subtract"] = "−",
        ["Add"]      = "+",
        ["Decimal"]  = ".",
        ["NumPad0"]  = "0", ["NumPad1"] = "1", ["NumPad2"] = "2", ["NumPad3"] = "3",
        ["NumPad4"]  = "4", ["NumPad5"] = "5", ["NumPad6"] = "6",
        ["NumPad7"]  = "7", ["NumPad8"] = "8", ["NumPad9"] = "9",

        // Media
        ["MediaPreviousTrack"] = "|◀",
        ["MediaPlayPause"]     = "▶||",
        ["MediaNextTrack"]     = "▶|",
        ["VolumeMute"]         = "Mute",
        ["VolumeDown"]         = "Vol−",
        ["VolumeUp"]           = "Vol+",
    };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is string name && Labels.TryGetValue(name, out var label) ? label : value?.ToString() ?? "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
