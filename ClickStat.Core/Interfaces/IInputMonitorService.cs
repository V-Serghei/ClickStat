using System.Windows.Forms;
using ClickStat.Core.Models;

namespace ClickStat.Core.Interfaces;

public interface IInputMonitorService
{
    event Action<Keys> OnKeyAction; // fires on KeyUp
    event Action<Keys> OnKeyDown;   // fires on KeyDown (for modifier tracking)

    void StartMonitoring();
    void StopMonitoring();
    void ResetStatistics();
}