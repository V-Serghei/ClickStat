using System.Windows;

namespace ClickStat.Presentation.Views;

public partial class TrainingResultWindow : Window
{
    public TrainingResultWindow() => InitializeComponent();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
