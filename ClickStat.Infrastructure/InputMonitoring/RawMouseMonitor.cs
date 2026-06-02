using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace ClickStat.Infrastructure.InputMonitoring;

/// <summary>
/// Captures physical mouse button presses via WM_INPUT (Raw Input API).
/// Works even when driver software (G Hub, Synapse, etc.) remaps buttons to keystrokes,
/// because Raw Input fires before the software intercepts the event.
/// </summary>
public sealed class RawMouseMonitor : IDisposable
{
    private const int    WM_INPUT          = 0x00FF;
    private const uint   RID_INPUT         = 0x10000003;
    private const uint   RIM_TYPEMOUSE     = 0;
    private const uint   RIDEV_INPUTSINK   = 0x00000100;

    private const ushort BTN1_DOWN  = 0x0001; // Left
    private const ushort BTN2_DOWN  = 0x0004; // Right
    private const ushort BTN3_DOWN  = 0x0010; // Middle
    private const ushort BTN4_DOWN  = 0x0040; // XButton1 / Back
    private const ushort BTN5_DOWN  = 0x0100; // XButton2 / Forward
    private const ushort RI_WHEEL   = 0x0400;

    /// <summary>Raw button number 1-5 (Left=1, Right=2, Middle=3, Back=4, Forward=5).</summary>
    public event Action<int>? ButtonDown;

    /// <summary>Positive = scroll up notches, negative = scroll down.</summary>
    public event Action<int>? Wheel;

    /// <summary>Cursor moved — raw delta (in HID units, relative). Used for distance tracking.</summary>
    public event Action<int, int>? Moved; // dx, dy

    private HwndSource? _source;

    public void Initialize(IntPtr hwnd)
    {
        _source = HwndSource.FromHwnd(hwnd);
        if (_source == null) return;

        _source.AddHook(WndProc);

        var device = new RAWINPUTDEVICE
        {
            UsagePage = 0x01, // Generic desktop controls
            Usage     = 0x02, // Mouse
            Flags     = RIDEV_INPUTSINK,
            Target    = hwnd
        };
        RegisterRawInputDevices(new[] { device }, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_INPUT) return IntPtr.Zero;

        uint headerSize = (uint)Marshal.SizeOf<RAWINPUTHEADER>();
        uint size = 0;
        GetRawInputData(lParam, RID_INPUT, IntPtr.Zero, ref size, headerSize);
        if (size == 0) return IntPtr.Zero;

        var buf = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputData(lParam, RID_INPUT, buf, ref size, headerSize) != size)
                return IntPtr.Zero;

            var header = Marshal.PtrToStructure<RAWINPUTHEADER>(buf);
            if (header.Type != RIM_TYPEMOUSE) return IntPtr.Zero;

            var mousePtr = new IntPtr(buf.ToInt64() + Marshal.SizeOf<RAWINPUTHEADER>());
            var mouse    = Marshal.PtrToStructure<RAWMOUSE>(mousePtr);

            ProcessMouse(mouse);
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
        return IntPtr.Zero;
    }

    private void ProcessMouse(RAWMOUSE m)
    {
        var f = m.ButtonFlags;

        // Movement (may accompany button events too)
        if ((m.LastX != 0 || m.LastY != 0) && Moved != null)
            Moved.Invoke(m.LastX, m.LastY);

        // Pure movement with no button/wheel — nothing else to process
        if (f == 0) return;

        if ((f & BTN1_DOWN) != 0) ButtonDown?.Invoke(1);
        if ((f & BTN2_DOWN) != 0) ButtonDown?.Invoke(2);
        if ((f & BTN3_DOWN) != 0) ButtonDown?.Invoke(3);
        if ((f & BTN4_DOWN) != 0) ButtonDown?.Invoke(4);
        if ((f & BTN5_DOWN) != 0) ButtonDown?.Invoke(5);

        if ((f & RI_WHEEL) != 0)
        {
            int notches = (short)m.ButtonData / 120;
            if (notches != 0) Wheel?.Invoke(notches);
        }
    }

    public void Dispose()
    {
        _source?.RemoveHook(WndProc);
        _source = null;
    }

    // ── P/Invoke ────────────────────────────────────────────────────────────

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

    [StructLayout(LayoutKind.Explicit)]
    private struct RAWMOUSE
    {
        [FieldOffset(0)]  public ushort Flags;
        [FieldOffset(4)]  public ushort ButtonFlags;
        [FieldOffset(6)]  public ushort ButtonData;   // wheel delta
        [FieldOffset(8)]  public uint   RawButtons;
        [FieldOffset(12)] public int    LastX;
        [FieldOffset(16)] public int    LastY;
        [FieldOffset(20)] public uint   ExtraInformation;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(
        [MarshalAs(UnmanagedType.LPArray)] RAWINPUTDEVICE[] pRawInputDevices,
        uint uiNumDevices, uint cbSize);

    [DllImport("user32.dll")]
    private static extern uint GetRawInputData(
        IntPtr hRawInput, uint uiCommand,
        IntPtr pData, ref uint pcbSize, uint cbSizeHeader);
}
