using System;
using System.Windows.Forms;

namespace ClickStat.Core.Interfaces;

public interface IMouseMonitorService
{
    event Action<MouseButtons, int> OnButtonPressed;
    event Action<int> OnScroll;
    void StartMonitoring();
    void StopMonitoring();
}
