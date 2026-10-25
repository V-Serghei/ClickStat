using System.Windows;

namespace ClickStat.Presentation.Views;

public partial class AchievementDetailWindow : Window
{
    public AchievementDetailWindow() => InitializeComponent();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
