using System;
using System.Windows.Forms;

namespace ClickStat.Core.Interfaces;

public interface IMouseMonitorService
{
    event Action<MouseButtons, int> OnButtonPressed;
    event Action<int> OnScroll;
    event Action<long, int, int> OnMoved;
    void StartMonitoring();
    void StopMonitoring();
}
