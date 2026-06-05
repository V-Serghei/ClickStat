using System.Windows.Forms;
using ClickStat.Core.Models;

namespace ClickStat.Core.Interfaces;

public interface IInputMonitorService
{
    event Action<Keys> OnKeyAction; // fires once for a counted key press
    event Action<Keys> OnKeyDown;   // fires before OnKeyAction, for modifiers/words
    event Action<Keys> OnKeyUp;     // releases modifier state

    void InitializeRawInput(IntPtr hwnd);
    void StartMonitoring();
    void StopMonitoring();
    void ResetStatistics();
}
