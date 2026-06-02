using System.Windows.Forms;

namespace ClickStat.Infrastructure.Services;

/// <summary>
/// Lightweight helper to query the currently active keyboard layout.
/// Reads InputLanguage.CurrentInputLanguage — no hooks, no interop needed.
/// </summary>
public static class LayoutService
{
    public static (string Code, string Name) GetCurrent()
    {
        var lang = InputLanguage.CurrentInputLanguage;
        string code = lang.Culture.TwoLetterISOLanguageName.ToUpperInvariant();
        string name = lang.Culture.NativeName;
        return (code, name);
    }
}
