using System.Windows.Forms;

namespace ClickStat.Core.Helpers;

/// <summary>
/// Encodes keyboard shortcuts as virtual mouse button codes.
/// Positive codes (e.g. 1048576) = real MouseButtons enum values.
/// Codes with bit 0x40000000 set = keyboard-mapped virtual buttons.
/// </summary>
public static class MouseButtonCodeHelper
{
    private const int KeyboardFlag = 0x40000000;

    public static int EncodeKeyboard(Keys key, bool ctrl, bool shift, bool alt)
    {
        int mods = (ctrl  ? 1 : 0)
                 | (shift ? 2 : 0)
                 | (alt   ? 4 : 0);
        return KeyboardFlag | (mods << 16) | (int)(key & Keys.KeyCode);
    }

    public static bool IsKeyboardMapped(int code) => (code & KeyboardFlag) != 0;

    public static (Keys key, bool ctrl, bool shift, bool alt) Decode(int code)
    {
        int mods    = (code >> 16) & 0xFF;
        var key     = (Keys)(code & 0xFFFF);
        return (key, (mods & 1) != 0, (mods & 2) != 0, (mods & 4) != 0);
    }

    public static string FormatShortcut(int code)
    {
        var (key, ctrl, shift, alt) = Decode(code);
        var parts = new System.Collections.Generic.List<string>();
        if (ctrl)  parts.Add("Ctrl");
        if (alt)   parts.Add("Alt");
        if (shift) parts.Add("Shift");
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }

    public static bool IsModifier(Keys key) => key is
        Keys.ControlKey or Keys.LControlKey or Keys.RControlKey or
        Keys.ShiftKey   or Keys.LShiftKey   or Keys.RShiftKey   or
        Keys.Menu       or Keys.LMenu       or Keys.RMenu;
}
