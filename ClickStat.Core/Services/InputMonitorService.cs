using System;
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
        InputLog.Key(source, "UP", key);

        var normalized = NormalizeKey(key);
        lock (_gate)
        {
            if (ShouldSuppressDuplicateCore(normalized, isKeyUp: true, source, DateTime.UtcNow))
            {
                InputLog.Suppress(source, "UP", key, "duplicate key-up");
                return;
            }

            _pressedKeys.Remove(normalized);
        }

        InputLog.Emit("UP", key);
        EnqueueEvent(() => OnKeyUp?.Invoke(key));
    }

    private bool ShouldCountKeyDown(Keys key, string source)
    {
        var now = DateTime.UtcNow;
        var normalized = NormalizeKey(key);

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

    private static Keys NormalizeKey(Keys key)
    {
        return key & Keys.KeyCode;
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
                InputLog.Info($"Input event processing failed: {ex.Message}");
            }
        }
    }
}
