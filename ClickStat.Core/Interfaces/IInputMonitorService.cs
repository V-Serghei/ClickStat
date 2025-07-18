using System.Windows.Forms;
using ClickStat.Core.Models;

namespace ClickStat.Core.Interfaces;

public interface IInputMonitorService
{
    event Action<Keys> OnKeyAction;

    void StartMonitoring();
    void StopMonitoring();
    void ResetStatistics();
}