using System.Diagnostics;
using ClickStat.Core.Interfaces;
using Microsoft.Win32;

namespace ClickStat.Core.Services;

public class StartupService: IStartupService
{
    private const string AppName = "ClickStat";
    private readonly RegistryKey _registryKey;
    private readonly string _appPath;

    public StartupService()
    {
        _registryKey = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true)!;
        _appPath = Process.GetCurrentProcess().MainModule!.FileName;
    }

    public bool IsInStartup()
    {
        var value = _registryKey.GetValue(AppName) as string;
        return value != null && value == _appPath;
    }

    public void AddToStartup()
    {
        if (!IsInStartup())
        {
            _registryKey.SetValue(AppName, _appPath);
        }
    }

    public void RemoveFromStartup()
    {
        if (IsInStartup())
        {
            _registryKey.DeleteValue(AppName, false);
        }
    }
}
