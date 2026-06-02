using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;

namespace ClickStat.Presentation.Converters;

public class KeyNameToLabelConverter : IValueConverter, IMultiValueConverter
{
    /// <summary>Set by KeyboardViewModel when layout changes. Thread-safe (UI only).</summary>
    public static string CurrentLayoutCode { get; set; } = "EN";

    // ── English / universal labels ────────────────────────────────────────
    private static readonly Dictionary<string, string> Labels = new()
    {
        ["Escape"]      = "Esc",    ["Back"]       = "⌫",
        ["Tab"]         = "Tab",    ["Capital"]    = "Caps",
        ["Enter"]       = "↵",      ["Space"]      = "Space",
        ["LShiftKey"]   = "Shift",  ["RShiftKey"]  = "Shift",
        ["LControlKey"] = "Ctrl",   ["RControlKey"]= "Ctrl",
        ["LWin"]        = "Win",    ["RWin"]       = "Win",
        ["LMenu"]       = "Alt",    ["RMenu"]      = "Alt",
        ["Oemtilde"]    = "` ~",
        ["D1"]="1",["D2"]="2",["D3"]="3",["D4"]="4",["D5"]="5",
        ["D6"]="6",["D7"]="7",["D8"]="8",["D9"]="9",["D0"]="0",
        ["OemMinus"]    = "- _",    ["Oemplus"]    = "= +",
        ["Oem4"]        = "[ {",    ["Oem6"]       = "] }",
        ["OemPipe"]     = "\\ |",   ["OemSemicolon"]= "; :",
        ["OemQuotes"]   = "' \"",   ["Oemcomma"]   = ", <",
        ["OemPeriod"]   = ". >",    ["Oem2"]       = "/ ?",
        ["Insert"]      = "Ins",    ["Delete"]     = "Del",
        ["Next"]        = "PgDn",   ["PageUp"]     = "PgUp",
        ["PrintScreen"] = "PrtSc",  ["LaunchApplication2"] = "ScrLk",
        ["MediaStop"]   = "Pause",  ["Scroll"]     = "ScrLk",
        ["NumLock"]     = "Num",    ["Clear"]      = "Clr",
        ["Divide"]      = "/",      ["Multiply"]   = "*",
        ["Subtract"]    = "−",      ["Add"]        = "+",
        ["Decimal"]     = ".",
        ["NumPad0"]="0",["NumPad1"]="1",["NumPad2"]="2",["NumPad3"]="3",
        ["NumPad4"]="4",["NumPad5"]="5",["NumPad6"]="6",
        ["NumPad7"]="7",["NumPad8"]="8",["NumPad9"]="9",
        ["Up"]="↑",["Down"]="↓",["Left"]="←",["Right"]="→",
        ["MediaPreviousTrack"]="|◀",["MediaPlayPause"]="▶||",["MediaNextTrack"]="▶|",
        ["VolumeMute"]="Mute",["VolumeDown"]="Vol−",["VolumeUp"]="Vol+",
    };

    // ── Russian (ЙЦУКЕН) layout — QWERTY key → Cyrillic ──────────────────
    private static readonly Dictionary<string, string> RuLabels = new()
    {
        ["Q"]="Й",["W"]="Ц",["E"]="У",["R"]="К",["T"]="Е",
        ["Y"]="Н",["U"]="Г",["I"]="Ш",["O"]="Щ",["P"]="З",
        ["A"]="Ф",["S"]="Ы",["D"]="В",["F"]="А",["G"]="П",
        ["H"]="Р",["J"]="О",["K"]="Л",["L"]="Д",
        ["Z"]="Я",["X"]="Ч",["C"]="С",["V"]="М",
        ["B"]="И",["N"]="Т",["M"]="Ь",
        ["Oem4"]="Х",["Oem6"]="Ъ",["OemSemicolon"]="Ж",
        ["Oemcomma"]="Б",["OemPeriod"]="Ю",["OemQuotes"]="Э",
        ["Oemtilde"]="Ё",["OemPipe"]="\\ /",
    };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string keyName) return "";
        return GetLabel(keyName, CurrentLayoutCode);
    }

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length == 0 || values[0] is not string keyName) return "";

        var layoutCode = values.Length > 1 && values[1] is string code
            ? code
            : CurrentLayoutCode;

        return GetLabel(keyName, layoutCode);
    }

    private static string GetLabel(string keyName, string layoutCode)
    {
        // For Russian layout: override letter and some symbol keys
        if (layoutCode == "RU" && RuLabels.TryGetValue(keyName, out var ru))
            return ru;

        return Labels.TryGetValue(keyName, out var label) ? label : keyName;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
