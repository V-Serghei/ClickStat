using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ClickStat.Infrastructure.Services;

/// <summary>
/// Lightweight helper to query the currently active keyboard layout.
/// Uses the foreground window thread layout and falls back to WinForms.
/// </summary>
public static class LayoutService
{
    public static (string Code, string Name) GetCurrent()
    {
        try
        {
            var culture = GetCurrentCulture();
            string code = culture.TwoLetterISOLanguageName.ToUpperInvariant();
            string name = culture.NativeName;
            return (code, name);
        }
        catch
        {
            var lang = InputLanguage.CurrentInputLanguage;
            string code = lang.Culture.TwoLetterISOLanguageName.ToUpperInvariant();
            string name = lang.Culture.NativeName;
            return (code, name);
        }
    }

    public static IntPtr GetCurrentKeyboardLayoutHandle()
    {
        var hwnd = GetForegroundWindow();
        var threadId = hwnd != IntPtr.Zero ? GetWindowThreadProcessId(hwnd, IntPtr.Zero) : 0;
        return GetKeyboardLayout(threadId);
    }

    private static CultureInfo GetCurrentCulture()
    {
        var layout = GetCurrentKeyboardLayoutHandle();
        var cultureId = unchecked((int)((long)layout & 0xffff));
        return CultureInfo.GetCultureInfo(cultureId);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint idThread);
}
