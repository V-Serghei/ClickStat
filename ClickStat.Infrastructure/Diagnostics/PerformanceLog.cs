using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Channels;

namespace ClickStat.Infrastructure.Diagnostics;

/// <summary>Small, bounded diagnostic stream. Never writes on an input hook or UI thread.</summary>
public static class PerformanceLog
{
    private const long MaxLogBytes = 8 * 1024 * 1024;
    private static readonly Channel<string> Lines = Channel.CreateBounded<string>(
        new BoundedChannelOptions(2048)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });
    private static readonly Task Writer = Task.Run(WriteLoopAsync);
    private static readonly Task Rates = Task.Run(WriteRatesAsync);
    private static long _keys;
    private static long _mouseMoves;
    private static long _uiDispatches;
    private static long _words;

    public static void CountKey() => Interlocked.Increment(ref _keys);
    public static void CountMouseMove() => Interlocked.Increment(ref _mouseMoves);
    public static void CountUiDispatch() => Interlocked.Increment(ref _uiDispatches);
    public static void CountWord() => Interlocked.Increment(ref _words);

    public static IDisposable Measure(string operation)
    {
        Record("start", operation);
        return new Measurement(operation);
    }

    public static void Record(string kind, string operation, long? elapsedMs = null)
    {
        try
        {
            Lines.Writer.TryWrite(JsonSerializer.Serialize(new
            {
                time = DateTimeOffset.UtcNow,
                kind,
                operation,
                elapsedMs,
                thread = Environment.CurrentManagedThreadId
            }));
        }
        catch { /* Diagnostics must not affect application behavior. */ }
    }

    private sealed class Measurement(string operation) : IDisposable
    {
        private readonly Stopwatch _watch = Stopwatch.StartNew();
        public void Dispose()
        {
            _watch.Stop();
            Record("end", operation, _watch.ElapsedMilliseconds);
        }
    }

    private static async Task WriteRatesAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        while (await timer.WaitForNextTickAsync())
        {
            Lines.Writer.TryWrite(JsonSerializer.Serialize(new
            {
                time = DateTimeOffset.UtcNow,
                kind = "rates",
                keys = Interlocked.Exchange(ref _keys, 0),
                mouseMoves = Interlocked.Exchange(ref _mouseMoves, 0),
                uiDispatches = Interlocked.Exchange(ref _uiDispatches, 0),
                words = Interlocked.Exchange(ref _words, 0)
            }));
        }
    }

    private static async Task WriteLoopAsync()
    {
        try
        {
            string directory = Environment.GetEnvironmentVariable("CLICKSTAT_DIAGNOSTICS_DIR") ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "KeyClick", "diagnostics");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "operations.jsonl");
            await foreach (var line in Lines.Reader.ReadAllAsync())
            {
                try
                {
                    if (File.Exists(path) && new FileInfo(path).Length > MaxLogBytes)
                        File.Move(path, path + ".1", overwrite: true);
                    await File.AppendAllTextAsync(path, line + Environment.NewLine);
                }
                catch { /* Disk failures should not interrupt input capture. */ }
            }
        }
        catch { /* The logger is optional. */ }
    }
}
