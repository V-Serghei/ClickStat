using System.Diagnostics;
using ClickStat.Core.Interfaces;
using Microsoft.Win32;

namespace ClickStat.Core.Services;

public class StartupService : IStartupService
{
    private const string AppName = "ClickStat";
    private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private readonly string _appPath;

    public StartupService()
    {
        _appPath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName;
    }

    public bool IsInStartup()
    {
        using var run = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        var command = (run?.GetValue(AppName) as string)?.Trim().Trim('"');
        if (!string.Equals(command, _appPath, StringComparison.OrdinalIgnoreCase)) return false;

        using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKeyPath);
        var state = approved?.GetValue(AppName) as byte[];
        // Windows treats a Run entry without StartupApproved metadata as enabled.
        return state is not { Length: > 0 } || state[0] is 0x02 or 0x06;
    }

    public void AddToStartup()
    {
        using var run = Registry.CurrentUser.CreateSubKey(RunKeyPath, true)
            ?? throw new InvalidOperationException("Cannot open the Windows startup registry key.");
        run.SetValue(AppName, $"\"{_appPath}\"", RegistryValueKind.String);

        // Task Manager can disable a Run entry without removing it. Re-enable it
        // when the user explicitly checks the application's startup option.
        using var approved = Registry.CurrentUser.CreateSubKey(ApprovedKeyPath, true)
            ?? throw new InvalidOperationException("Cannot open Windows startup approval.");
        var enabled = new byte[12];
        enabled[0] = 0x02;
        approved.SetValue(AppName, enabled, RegistryValueKind.Binary);
    }

    public void RemoveFromStartup()
    {
        using var run = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
        run?.DeleteValue(AppName, false);
        using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKeyPath, true);
        approved?.DeleteValue(AppName, false);
    }
}
