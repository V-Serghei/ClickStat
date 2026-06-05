using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Interop;

namespace ClickStat.Infrastructure.InputMonitoring;

/// <summary>
/// Captures physical keyboard input through WM_INPUT. This is more reliable than
/// low-level hooks for focused shells like Remote Desktop Connection.
/// </summary>
public sealed class RawKeyboardMonitor : IDisposable
{
    private const int  WM_INPUT          = 0x00FF;
    private const int  WM_KEYDOWN        = 0x0100;
    private const int  WM_KEYUP          = 0x0101;
    private const int  WM_SYSKEYDOWN     = 0x0104;
    private const int  WM_SYSKEYUP       = 0x0105;
    private const uint RID_INPUT         = 0x10000003;
    private const uint RIM_TYPEKEYBOARD  = 1;
    private const uint RIDEV_INPUTSINK   = 0x00000100;

    public event Action<Keys>? KeyDown;
    public event Action<Keys>? KeyUp;

    private HwndSource? _source;

    public bool Initialize(IntPtr hwnd)
    {
        var source = HwndSource.FromHwnd(hwnd);
        if (source == null) return false;

        var device = new RAWINPUTDEVICE
        {
            UsagePage = 0x01, // Generic desktop controls
            Usage     = 0x06, // Keyboard
            Flags     = RIDEV_INPUTSINK,
            Target    = hwnd
        };

        if (!RegisterRawInputDevices(new[] { device }, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
            return false;

        _source = source;
        _source.AddHook(WndProc);
        return true;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_INPUT) return IntPtr.Zero;

        uint headerSize = (uint)Marshal.SizeOf<RAWINPUTHEADER>();
        uint size = 0;
        GetRawInputData(lParam, RID_INPUT, IntPtr.Zero, ref size, headerSize);
        if (size == 0) return IntPtr.Zero;

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputData(lParam, RID_INPUT, buffer, ref size, headerSize) != size)
                return IntPtr.Zero;

            var header = Marshal.PtrToStructure<RAWINPUTHEADER>(buffer);
            if (header.Type != RIM_TYPEKEYBOARD) return IntPtr.Zero;

            var keyboardPtr = IntPtr.Add(buffer, Marshal.SizeOf<RAWINPUTHEADER>());
            var keyboard = Marshal.PtrToStructure<RAWKEYBOARD>(keyboardPtr);
            ProcessKeyboard(keyboard);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return IntPtr.Zero;
    }

    private void ProcessKeyboard(RAWKEYBOARD keyboard)
    {
        var key = (Keys)keyboard.VKey;
        if (key == Keys.None) return;

        if (keyboard.Message is WM_KEYDOWN or WM_SYSKEYDOWN)
            KeyDown?.Invoke(key);
        else if (keyboard.Message is WM_KEYUP or WM_SYSKEYUP)
            KeyUp?.Invoke(key);
    }

    public void Dispose()
    {
        _source?.RemoveHook(WndProc);
        _source = null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICE
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint   Flags;
        public IntPtr Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTHEADER
    {
        public uint   Type;
        public uint   Size;
        public IntPtr Device;
        public IntPtr WParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWKEYBOARD
    {
        public ushort MakeCode;
        public ushort Flags;
        public ushort Reserved;
        public ushort VKey;
        public uint   Message;
        public uint   ExtraInformation;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(
        [MarshalAs(UnmanagedType.LPArray)] RAWINPUTDEVICE[] pRawInputDevices,
        uint uiNumDevices,
        uint cbSize);

    [DllImport("user32.dll")]
    private static extern uint GetRawInputData(
        IntPtr hRawInput,
        uint uiCommand,
        IntPtr pData,
        ref uint pcbSize,
        uint cbSizeHeader);
}
