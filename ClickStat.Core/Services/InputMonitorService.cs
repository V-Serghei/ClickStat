using System;
using System.Windows.Forms;
using ClickStat.Core.Interfaces;
using ClickStat.Infrastructure.Diagnostics;
using ClickStat.Infrastructure.InputMonitoring;

namespace ClickStat.Core.Services
{
    /// <summary>
    /// Keyboard input monitor.
    ///
    /// Architecture:
    ///   PRIMARY  — WH_KEYBOARD_LL global hook (KeyboardMonitor).
    ///              Catches all keystrokes including injected ones from RDP.
    ///   SECONDARY — WM_INPUT raw keyboard (RawKeyboardMonitor, optional).
    ///              Helps on some systems where the low-level hook is unreliable.
    ///              A 75 ms dedup window prevents double-counting when both fire.
    ///
    /// Events fire on the UI (hook) thread — safe for WPF bindings.
    /// </summary>
    public class InputMonitorService : IInputMonitorService
    {
        private readonly KeyboardMonitor    _hookMonitor;
        private readonly RawKeyboardMonitor _rawMonitor;
        private bool _hookActive;

        // Dedup: only suppress when SAME key fires from BOTH sources within 75 ms.
        // Uses DateTime (UTC) for minimal overhead.
        private readonly object _dedupLock = new();
        private readonly Dictionary<Keys, (string source, DateTime time)> _lastKeyUp  = new();
        private readonly Dictionary<Keys, (string source, DateTime time)> _lastKeyDown = new();
        private static readonly TimeSpan DedupWindow = TimeSpan.FromMilliseconds(75);

        public event Action<Keys>? OnKeyAction; // KeyUp — used for keystroke counting
        public event Action<Keys>? OnKeyDown;   // KeyDown — used for modifier tracking
        public event Action<Keys>? OnKeyUp;     // alias for OnKeyAction (same event, KeyUp)

        public InputMonitorService()
        {
            _hookMonitor = new KeyboardMonitor();
            _hookMonitor.KeyPressed += key => HandleKeyUp(key, "hook");
            _hookMonitor.KeyDown    += key => HandleKeyDown(key, "hook");

            _rawMonitor = new RawKeyboardMonitor();
            _rawMonitor.KeyUp   += key => HandleKeyUp(key, "raw");
            _rawMonitor.KeyDown += key => HandleKeyDown(key, "raw");

            InputLog.Info("InputMonitorService created");
        }

        public void InitializeRawInput(IntPtr hwnd)
        {
            bool ok = _rawMonitor.Initialize(hwnd);
            InputLog.Info($"RawKeyboardMonitor.Initialize → {ok}");
        }

        public void StartMonitoring()
        {
            if (_hookActive) return;
            _hookMonitor.Subscribe();
            _hookActive = true;
            InputLog.Info("WH_KEYBOARD_LL hook subscribed");
        }

        public void StopMonitoring()
        {
            if (!_hookActive) return;
            _hookMonitor.Unsubscribe();
            _hookActive = false;
        }

        public void ResetStatistics() { }

        // ── Event handlers ────────────────────────────────────────────────────

        private void HandleKeyDown(Keys key, string source)
        {
            InputLog.Key(source, "DOWN", key);

            if (IsDuplicate(_lastKeyDown, key, source)) return;

            InputLog.Emit("DOWN", key);
            OnKeyDown?.Invoke(key);
        }

        private void HandleKeyUp(Keys key, string source)
        {
            InputLog.Key(source, "UP", key);

            if (IsDuplicate(_lastKeyUp, key, source)) return;

            InputLog.Emit("UP  ", key);
            OnKeyAction?.Invoke(key);
            OnKeyUp?.Invoke(key);
        }

        // ── Deduplication ─────────────────────────────────────────────────────
        // Suppress only when the EXACT SAME key fired from the OTHER source
        // within DedupWindow.  If both sources fire → exactly one event passes.
        // If only one source fires (RDP, some remote shells) → event always passes.

        private bool IsDuplicate(
            Dictionary<Keys, (string source, DateTime time)> table,
            Keys key, string source)
        {
            var now = DateTime.UtcNow;
            lock (_dedupLock)
            {
                if (table.TryGetValue(key, out var last) &&
                    last.source != source &&
                    now - last.time <= DedupWindow)
                {
                    InputLog.Suppress(source, "    ", key,
                        $"dup of '{last.source}' {(now - last.time).TotalMilliseconds:0}ms ago");
                    return true;
                }
                table[key] = (source, now);
                return false;
            }
        }
    }
}
