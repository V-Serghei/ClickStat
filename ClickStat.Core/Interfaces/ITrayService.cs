using System.Windows;
using System.Windows.Forms.VisualStyles;

namespace ClickStat.Core.Interfaces;

public interface ITrayService
{
    void Initialize(Window window);
}