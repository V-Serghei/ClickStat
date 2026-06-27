using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using ClickStat.Presentation.ViewModels;

namespace ClickStat.Presentation;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(ClearButtonFocusAfterClick), true);
    }

    private void ClearButtonFocusAfterClick(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(() => Keyboard.ClearFocus(), DispatcherPriority.Background);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseButton_Click(object sender, RoutedEventArgs e)
        => Close();
}
