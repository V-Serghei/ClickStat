using System.Windows;
using System.Windows.Forms.VisualStyles;

namespace ClickStat.Core.Interfaces;

public interface ITrayService
{
    event Action<string>? AchievementNotificationClicked;
    void Initialize(Window window);
    void ShowNotification(string title, string message, string? achievementId = null);
}
