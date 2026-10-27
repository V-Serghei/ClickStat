using System.Windows.Controls;
using System.Windows;
using System.Windows.Input;
using ClickStat.Presentation.ViewModels;

namespace ClickStat.Presentation.Views;

public partial class AchievementsView : UserControl
{
    public AchievementsView() => InitializeComponent();

    private void Achievement_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AchievementCard card }
            && DataContext is AchievementsViewModel vm)
            _ = vm.OpenDetailAsync(card.Id);
    }
}
