using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace ClickStat.Diagnostics;

internal static class Program
{
    private const int SampleSeconds = 2;
    private const int StartupSampleMilliseconds = 500;
    private const int StartupSampleSeconds = 20;
    private const int ProcessSnapshotSeconds = 30;
    private const long MaxLogBytes = 12 * 1024 * 1024;
    private static readonly string LogDirectory =
        Environment.GetEnvironmentVariable("CLICKSTAT_DIAGNOSTICS_DIR") ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "KeyClick", "diagnostics");

    private static async Task<int> Main(string[] args)
    {
        if (args.Length != 2 || !int.TryParse(args[0], out var pid) ||
            !long.TryParse(args[1], out var startTicks)) return 2;

        try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; }
        catch { /* Diagnostics should still run with limited permissions. */ }

        Directory.CreateDirectory(LogDirectory);
        string path = Path.Combine(LogDirectory, "resources.jsonl");
        Write(path, new { kind = "monitor_start", time = DateTimeOffset.UtcNow, pid,
            targetStartedAtUtc = new DateTime(startTicks, DateTimeKind.Utc) });

        var previousSystem = ReadSystemTimes();
        TimeSpan previousTargetCpu = TimeSpan.Zero;
        try
        {
            using var initialTarget = Process.GetProcessById(pid);
            previousTargetCpu = initialTarget.TotalProcessorTime;
        }
        catch { /* The target may have exited before monitoring begins. */ }
        IO_COUNTERS previousIo = default;
        bool hasPreviousIo = false;
        DateTime previousTime = DateTime.UtcNow;
        DateTime monitorStarted = previousTime;
        var previousProcesses = new Dictionary<int, TimeSpan>();
        DateTime lastProcessSnapshot = DateTime.UtcNow;
        int missingSamples = 0;

        while (true)
        {
            await Task.Delay(DateTime.UtcNow - monitorStarted < TimeSpan.FromSeconds(StartupSampleSeconds)
                ? TimeSpan.FromMilliseconds(StartupSampleMilliseconds)
                : TimeSpan.FromSeconds(SampleSeconds));
            Process? target = null;
            try
            {
                target = Process.GetProcessById(pid);
                if (target.HasExited || target.StartTime.ToUniversalTime().Ticks != startTicks)
                    throw new InvalidOperationException("Target process exited or PID was reused.");

                target.Refresh();
                missingSamples = 0;
                var now = DateTime.UtcNow;
                var system = ReadSystemTimes();
                var targetCpu = target.TotalProcessorTime;
                bool hasIo = GetProcessIoCounters(target.Handle, out var io);
                double elapsed = Math.Max(0.001, (now - previousTime).TotalSeconds);
                double appCpuPercent = Math.Clamp(
                    (targetCpu - previousTargetCpu).TotalSeconds / elapsed /
                    Environment.ProcessorCount * 100, 0, 100);
                double systemCpuPercent = SystemCpuPercent(previousSystem, system);
                var memory = ReadMemoryStatus();

                Write(path, new
                {
                    kind = "sample", time = DateTimeOffset.UtcNow, pid,
                    appCpuPercent = Math.Round(appCpuPercent, 1),
                    systemCpuPercent = Math.Round(systemCpuPercent, 1),
                    systemMemoryPercent = memory.loadPercent,
                    availableMemoryMb = memory.availableMb,
                    workingSetMb = Math.Round(target.WorkingSet64 / 1048576.0, 1),
                    privateMb = Math.Round(target.PrivateMemorySize64 / 1048576.0, 1),
                    readMbPerSecond = hasIo && hasPreviousIo ? Math.Round(Delta(io.ReadTransferCount, previousIo.ReadTransferCount) / 1048576.0 / elapsed, 2) : 0,
                    writeMbPerSecond = hasIo && hasPreviousIo ? Math.Round(Delta(io.WriteTransferCount, previousIo.WriteTransferCount) / 1048576.0 / elapsed, 2) : 0,
                    threads = target.Threads.Count,
                    handles = target.HandleCount,
                    responding = TryResponding(target)
                });

                bool spike = appCpuPercent >= 12 || systemCpuPercent >= 85;
                double sinceSnapshot = (now - lastProcessSnapshot).TotalSeconds;
                if ((spike && sinceSnapshot >= 10) || sinceSnapshot >= ProcessSnapshotSeconds)
                {
                    previousProcesses = WriteProcessSnapshot(path, previousProcesses, sinceSnapshot);
                    lastProcessSnapshot = now;
                }

                previousSystem = system;
                previousTargetCpu = targetCpu;
                if (hasIo) { previousIo = io; hasPreviousIo = true; }
                previousTime = now;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
            {
                if (++missingSamples >= 2)
                {
                    Write(path, new { kind = "monitor_stop", time = DateTimeOffset.UtcNow, pid, reason = ex.Message });
                    return 0;
                }
            }
            catch (Exception ex)
            {
                Write(path, new { kind = "monitor_error", time = DateTimeOffset.UtcNow, error = ex.GetType().Name });
            }
            finally { target?.Dispose(); }
        }
    }

    private static bool? TryResponding(Process process)
    {
        try
        {
            IntPtr window = process.MainWindowHandle;
            if (window == IntPtr.Zero) return null;
            return SendMessageTimeout(window, 0, UIntPtr.Zero, IntPtr.Zero, 0x0003, 200,
                out _) != IntPtr.Zero;
        }
        catch { return null; }
    }

    private static Dictionary<int, TimeSpan> WriteProcessSnapshot(
        string path, Dictionary<int, TimeSpan> previous, double elapsedSeconds)
    {
        var next = new Dictionary<int, TimeSpan>();
        var rows = new List<ProcessSample>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    var cpu = process.TotalProcessorTime;
                    next[process.Id] = cpu;
                    double percent = previous.TryGetValue(process.Id, out var old)
                        ? Math.Clamp((cpu - old).TotalSeconds / Math.Max(0.001, elapsedSeconds) /
                                     Environment.ProcessorCount * 100, 0, 100)
                        : 0;
                    rows.Add(new ProcessSample(process.Id, process.ProcessName,
                        Math.Round(percent, 1),
                        Math.Round(process.WorkingSet64 / 1048576.0, 1)));
                }
                catch { /* Processes can exit or deny access while enumerating. */ }
            }
        }
        Write(path, new { kind = "processes", time = DateTimeOffset.UtcNow,
            count = rows.Count, processes = rows.OrderByDescending(p => p.CpuPercent).ToArray() });
        return next;
    }

    private sealed record ProcessSample(int Pid, string Name, double CpuPercent, double WorkingSetMb);

    private static double SystemCpuPercent((ulong idle, ulong total) before, (ulong idle, ulong total) after)
    {
        ulong total = after.total >= before.total ? after.total - before.total : 0;
        ulong idle = after.idle >= before.idle ? after.idle - before.idle : 0;
        return total == 0 ? 0 : Math.Clamp((double)(total - Math.Min(total, idle)) / total * 100, 0, 100);
    }

    private static (ulong idle, ulong total) ReadSystemTimes()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return (0, 0);
        ulong idleTime = ToUInt64(idle);
        return (idleTime, ToUInt64(kernel) + ToUInt64(user));
    }

    private static (uint loadPercent, long availableMb) ReadMemoryStatus()
    {
        var state = new MEMORYSTATUSEX { Length = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        return GlobalMemoryStatusEx(ref state)
            ? (state.MemoryLoad, (long)(state.AvailablePhysical / 1048576))
            : (0, 0);
    }

    private static ulong ToUInt64(FILETIME value) => ((ulong)value.High << 32) | value.Low;
    private static ulong Delta(ulong current, ulong previous) => current >= previous ? current - previous : 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME { public uint Low; public uint High; }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX state);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessIoCounters(IntPtr process, out IO_COUNTERS counters);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, UIntPtr wParam,
        IntPtr lParam, uint flags, uint timeoutMs, out UIntPtr result);

    private static void Write(string path, object item)
    {
        try
        {
            if (File.Exists(path) && new FileInfo(path).Length > MaxLogBytes)
            {
                string archive = path + ".1";
                File.Move(path, archive, overwrite: true);
            }
            File.AppendAllText(path, JsonSerializer.Serialize(item) + Environment.NewLine);
        }
        catch { /* Monitoring must never affect ClickStat. */ }
    }
}
