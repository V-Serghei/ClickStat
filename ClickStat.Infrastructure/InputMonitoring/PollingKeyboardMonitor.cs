using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ClickStat.Infrastructure.InputMonitoring;

/// <summary>
/// Polls GetAsyncKeyState as a fallback for clients like mstsc that can drop hook/raw events.
/// Emits only up/down transitions, not every polling tick.
/// </summary>
public sealed class PollingKeyboardMonitor : IDisposable
{
    private static readonly Keys[] KeysToPoll = Enumerable.Range(8, 248)
        .Where(IsKeyboardVirtualKey)
        .Select(vk => (Keys)vk)
        .ToArray();

    private readonly bool[] _down = new bool[256];
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public event Action<Keys>? KeyDown;
    public event Action<Keys>? KeyUp;

    public void Start()
    {
        if (_loop is { IsCompleted: false }) return;

        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => PollLoopAsync(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
    }

    private async Task PollLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            foreach (var key in KeysToPoll)
            {
                int vk = (int)key;
                bool isDown = (GetAsyncKeyState(vk) & 0x8000) != 0;
                if (isDown == _down[vk]) continue;

                _down[vk] = isDown;
                if (isDown)
                    KeyDown?.Invoke(key);
                else
                    KeyUp?.Invoke(key);
            }

            try
            {
                await Task.Delay(25, token);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    private static bool IsKeyboardVirtualKey(int vk)
    {
        if (vk is >= 0x01 and <= 0x07) return false; // mouse buttons / reserved
        if (vk is 0x10 or 0x11 or 0x12) return false; // use L/R modifiers instead
        if (vk is >= 0x0E and <= 0x0F) return false;
        return true;
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
