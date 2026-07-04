using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using ClickStat.Infrastructure.Services;

namespace ClickStat.Presentation.Services;

public static class KeyTextResolver
{
    public static char? ResolveChar(Keys key, bool shift)
    {
        var keyState = new byte[256];
        var keyCode = key & Keys.KeyCode;
        int virtualKey = (int)keyCode;
        if (virtualKey is <= 0 or > 255) return null;

        keyState[virtualKey] = 0x80;
        if ((GetKeyState((int)Keys.Capital) & 0x0001) != 0)
            keyState[(int)Keys.Capital] = 0x01;

        if (shift)
        {
            keyState[(int)Keys.ShiftKey] = 0x80;
            keyState[(int)Keys.LShiftKey] = 0x80;
        }

        var sb = new StringBuilder(4);
        var layout = LayoutService.GetCurrentKeyboardLayoutHandle();
        uint scanCode = (uint)MapVirtualKeyEx((uint)virtualKey, 0, layout);
        int result = ToUnicodeEx((uint)virtualKey, scanCode, keyState, sb, 4, 4, layout);
        return result > 0 && sb.Length > 0 ? sb[0] : null;
    }

    [DllImport("user32.dll")]
    private static extern int ToUnicodeEx(
        uint wVirtKey,
        uint wScanCode,
        byte[] lpKeyState,
        [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pwszBuff,
        int cchBuff,
        uint wFlags,
        IntPtr dwhkl);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKeyEx(uint uCode, uint uMapType, IntPtr dwhkl);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);
}
