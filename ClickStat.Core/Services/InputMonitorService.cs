using System;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using System.Windows.Forms;
using ClickStat.Core.Interfaces;
using ClickStat.Infrastructure.Diagnostics;
using ClickStat.Infrastructure.InputMonitoring;

namespace ClickStat.Core.Services;

/// <summary>
/// Combines three keyboard capture paths: low-level hook, Raw Input, and polling.
/// RDP/mstsc may expose only part of the input through any one API, so every path
/// goes through the same deduplication and counted-press pipeline.
/// </summary>
public class InputMonitorService : IInputMonitorService
{
    private readonly KeyboardMonitor _hookMonitor;
    private readonly RawKeyboardMonitor _rawMonitor;
    private readonly PollingKeyboardMonitor _pollMonitor;

    private readonly object _gate = new();
    private readonly Dictionary<(Keys Key, bool IsKeyUp), (string Source, DateTime Time)> _lastEmitted = new();
    private readonly HashSet<Keys> _pressedKeys = new();
    private readonly Dictionary<Keys, Keys> _genericModifierSide = new();
    private readonly Channel<Action> _eventQueue = Channel.CreateUnbounded<Action>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    private bool _hookActive;
    private bool _pollingActive;

    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromMilliseconds(75);

    public event Action<Keys>? OnKeyAction; // counted key press, emitted on accepted KeyDown
    public event Action<Keys>? OnKeyDown;   // emitted before OnKeyAction for modifiers/words
    public event Action<Keys>? OnKeyUp;     // modifier release only

    public InputMonitorService()
    {
        _hookMonitor = new KeyboardMonitor();
        _hookMonitor.KeyDown    += key => HandleKeyDown(key, "hook");
        _hookMonitor.KeyPressed += key => HandleKeyUp(key, "hook");

        _rawMonitor = new RawKeyboardMonitor();
        _rawMonitor.KeyDown += key => HandleKeyDown(key, "raw");
        _rawMonitor.KeyUp   += key => HandleKeyUp(key, "raw");

        _pollMonitor = new PollingKeyboardMonitor();
        _pollMonitor.KeyDown += key => HandleKeyDown(key, "poll");
        _pollMonitor.KeyUp   += key => HandleKeyUp(key, "poll");

        _ = ProcessEventQueueAsync();
        InputLog.Info("InputMonitorService created: hook + raw + poll");
    }

    public void InitializeRawInput(IntPtr hwnd)
    {
        bool ok = _rawMonitor.Initialize(hwnd);
        InputLog.Info($"RawKeyboardMonitor.Initialize -> {ok}");
    }

    public void StartMonitoring()
    {
        if (!_hookActive)
        {
            _hookMonitor.Subscribe();
            _hookActive = true;
            InputLog.Info("WH_KEYBOARD_LL hook subscribed");
        }

        if (!_pollingActive)
        {
            _pollMonitor.Start();
            _pollingActive = true;
            InputLog.Info("GetAsyncKeyState polling started");
        }
    }

    public void StopMonitoring()
    {
        if (_hookActive)
        {
            _hookMonitor.Unsubscribe();
            _hookActive = false;
            InputLog.Info("WH_KEYBOARD_LL hook unsubscribed");
        }

        if (_pollingActive)
        {
            _pollMonitor.Stop();
            _pollingActive = false;
            InputLog.Info("GetAsyncKeyState polling stopped");
        }
    }

    public void ResetStatistics() { }

    private void HandleKeyDown(Keys key, string source)
    {
        if (!IsRealKey(key)) return;
        key = NormalizeKey(key, isKeyDown: true);
        InputLog.Key(source, "DOWN", key);

        if (!ShouldCountKeyDown(key, source)) return;

        InputLog.Emit("DOWN", key);
        EnqueueEvent(() =>
        {
            OnKeyDown?.Invoke(key);
            OnKeyAction?.Invoke(key);
        });
    }

    private void HandleKeyUp(Keys key, string source)
    {
        if (!IsRealKey(key)) return;
        key = NormalizeKey(key, isKeyDown: false);
        InputLog.Key(source, "UP", key);

        var normalized = key;
        lock (_gate)
        {
            if (ShouldSuppressDuplicateCore(normalized, isKeyUp: true, source, DateTime.UtcNow))
            {
                InputLog.Suppress(source, "UP", key, "duplicate key-up");
                return;
            }

            _pressedKeys.Remove(normalized);
            ForgetGenericModifierSide(normalized);
        }

        InputLog.Emit("UP", key);
        EnqueueEvent(() => OnKeyUp?.Invoke(key));
    }

    private bool ShouldCountKeyDown(Keys key, string source)
    {
        var now = DateTime.UtcNow;
        var normalized = key;

        lock (_gate)
        {
            if (ShouldSuppressDuplicateCore(normalized, isKeyUp: false, source, now))
            {
                InputLog.Suppress(source, "DOWN", key, "duplicate key-down");
                return false;
            }

            if (_pressedKeys.Contains(normalized))
            {
                InputLog.Suppress(source, "DOWN", key, "already pressed");
                return false;
            }

            _pressedKeys.Add(normalized);
            RememberGenericModifierSide(normalized);
            return true;
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

    private static bool IsRealKey(Keys key)
    {
        int vk = (int)(key & Keys.KeyCode);
        return vk is > 0 and < 255;
    }

    private Keys NormalizeKey(Keys key, bool isKeyDown)
    {
        var keyCode = key & Keys.KeyCode;
        return keyCode switch
        {
            Keys.ShiftKey => ResolveGenericModifierSide(
                Keys.ShiftKey, Keys.LShiftKey, Keys.RShiftKey, isKeyDown),
            Keys.ControlKey => ResolveGenericModifierSide(
                Keys.ControlKey, Keys.LControlKey, Keys.RControlKey, isKeyDown),
            Keys.Menu => ResolveGenericModifierSide(
                Keys.Menu, Keys.LMenu, Keys.RMenu, isKeyDown),
            _ => keyCode
        };
    }

    private Keys ResolveGenericModifierSide(Keys generic, Keys left, Keys right, bool isKeyDown)
    {
        if (!isKeyDown)
        {
            lock (_gate)
            {
                if (_genericModifierSide.TryGetValue(generic, out var remembered))
                    return remembered;
            }
        }

        if (IsKeyDown(right)) return right;
        if (IsKeyDown(left)) return left;

        lock (_gate)
        {
            return _genericModifierSide.TryGetValue(generic, out var remembered) ? remembered : left;
        }
    }

    private void RememberGenericModifierSide(Keys key)
    {
        switch (key)
        {
            case Keys.LShiftKey or Keys.RShiftKey:
                _genericModifierSide[Keys.ShiftKey] = key;
                break;
            case Keys.LControlKey or Keys.RControlKey:
                _genericModifierSide[Keys.ControlKey] = key;
                break;
            case Keys.LMenu or Keys.RMenu:
                _genericModifierSide[Keys.Menu] = key;
                break;
        }
    }

    private void ForgetGenericModifierSide(Keys key)
    {
        switch (key)
        {
            case Keys.LShiftKey or Keys.RShiftKey:
                _genericModifierSide.Remove(Keys.ShiftKey);
                break;
            case Keys.LControlKey or Keys.RControlKey:
                _genericModifierSide.Remove(Keys.ControlKey);
                break;
            case Keys.LMenu or Keys.RMenu:
                _genericModifierSide.Remove(Keys.Menu);
                break;
        }
    }

    private static bool IsKeyDown(Keys key) => (GetAsyncKeyState((int)key) & 0x8000) != 0;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

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
                InputLog.Info($"Input event processing failed: {ex.Message}");
            }
        }
    }
}
