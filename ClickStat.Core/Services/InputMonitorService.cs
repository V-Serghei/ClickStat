using System;
using System.Threading.Channels;
using System.Windows.Forms;
using ClickStat.Core.Interfaces;
using ClickStat.Core.Models;
using ClickStat.Infrastructure.InputMonitoring;

namespace ClickStat.Core.Services
{
    public class InputMonitorService : IInputMonitorService
    {
        private readonly KeyboardMonitor _keyboardMonitor;
        private readonly RawKeyboardMonitor _rawKeyboardMonitor;
        private bool _hookSubscribed;
        private readonly object _emitLock = new();
        private readonly Dictionary<(Keys Key, bool IsKeyUp), (string Source, DateTime Time)> _lastEmitted = new();
        private readonly HashSet<Keys> _pressedKeys = new();
        private readonly Dictionary<Keys, DateTime> _lastCountedKeyDown = new();
        private readonly Channel<Action> _eventQueue = Channel.CreateUnbounded<Action>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        private static readonly TimeSpan DuplicateWindow = TimeSpan.FromMilliseconds(75);
        private static readonly TimeSpan StuckKeyRepeatWindow = TimeSpan.FromMilliseconds(180);

        public event Action<Keys>? OnKeyAction;
        public event Action<Keys>? OnKeyDown;
        public event Action<Keys>? OnKeyUp;

        public InputMonitorService()
        {
            _keyboardMonitor = new KeyboardMonitor();
            _keyboardMonitor.KeyPressed += key => EmitKeyUp(key, "hook");
            _keyboardMonitor.KeyDown    += key => EmitKeyDown(key, "hook");

            _rawKeyboardMonitor = new RawKeyboardMonitor();
            _rawKeyboardMonitor.KeyUp   += key => EmitKeyUp(key, "raw");
            _rawKeyboardMonitor.KeyDown += key => EmitKeyDown(key, "raw");

            _ = ProcessEventQueueAsync();
        }

        public void InitializeRawInput(IntPtr hwnd)
        {
            // Keep both Raw Input and the low-level hook alive. mstsc/RDP can expose
            // different parts of input through different APIs, so disabling one path
            // makes remote-session capture worse. EmitKey* deduplicates local doubles.
            _rawKeyboardMonitor.Initialize(hwnd);
        }

        public void StartMonitoring()
        {
            if (_hookSubscribed) return;
            _keyboardMonitor.Subscribe();
            _hookSubscribed = true;
        }

        public void StopMonitoring()
        {
            if (_hookSubscribed)
            {
                _keyboardMonitor.Unsubscribe();
                _hookSubscribed = false;
            }
        }

        public void ResetStatistics()
        {
        }

        private void EmitKeyDown(Keys key, string source)
        {
            if (!ShouldEmitKeyDown(key, source)) return;
            EnqueueEvent(() =>
            {
                OnKeyDown?.Invoke(key);
                OnKeyAction?.Invoke(key);
            });
        }

        private void EmitKeyUp(Keys key, string source)
        {
            if (ShouldSuppressDuplicate(key, isKeyUp: true, source)) return;

            lock (_emitLock)
            {
                _pressedKeys.Remove(NormalizeKey(key));
            }

            EnqueueEvent(() => OnKeyUp?.Invoke(key));
        }

        private bool ShouldEmitKeyDown(Keys key, string source)
        {
            var now = DateTime.UtcNow;
            var normalized = NormalizeKey(key);

            lock (_emitLock)
            {
                if (ShouldSuppressDuplicateCore(normalized, isKeyUp: false, source, now))
                    return false;

                if (_pressedKeys.Contains(normalized) &&
                    _lastCountedKeyDown.TryGetValue(normalized, out var lastDown) &&
                    now - lastDown < StuckKeyRepeatWindow)
                {
                    return false;
                }

                _pressedKeys.Add(normalized);
                _lastCountedKeyDown[normalized] = now;
                return true;
            }
        }

        private bool ShouldSuppressDuplicate(Keys key, bool isKeyUp, string source)
        {
            var now = DateTime.UtcNow;
            var normalized = NormalizeKey(key);

            lock (_emitLock)
            {
                return ShouldSuppressDuplicateCore(normalized, isKeyUp, source, now);
            }
        }

        private bool ShouldSuppressDuplicateCore(Keys key, bool isKeyUp, string source, DateTime now)
        {
            var signature = (key, isKeyUp);
            if (_lastEmitted.TryGetValue(signature, out var last) &&
                last.Source != source &&
                now - last.Time <= DuplicateWindow)
            {
                return true;
            }

            _lastEmitted[signature] = (source, now);
            return false;
        }

        private static Keys NormalizeKey(Keys key)
        {
            var keyCode = key & Keys.KeyCode;
            return keyCode switch
            {
                Keys.LShiftKey or Keys.RShiftKey => Keys.ShiftKey,
                Keys.LControlKey or Keys.RControlKey => Keys.ControlKey,
                Keys.LMenu or Keys.RMenu => Keys.Menu,
                _ => keyCode
            };
        }

        private void EnqueueEvent(Action action)
        {
            _eventQueue.Writer.TryWrite(action);
        }

        private async Task ProcessEventQueueAsync()
        {
            await foreach (var action in _eventQueue.Reader.ReadAllAsync())
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Input event processing failed: {ex.Message}");
                }
            }
        }
    }
}
