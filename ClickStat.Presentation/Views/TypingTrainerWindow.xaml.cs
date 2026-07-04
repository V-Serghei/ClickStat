using System.Windows;
using ClickStat.Presentation.ViewModels;

namespace ClickStat.Presentation.Views;

public partial class TypingTrainerWindow : Window
{
    public TypingTrainerWindow()
    {
        InitializeComponent();
        Closed += (_, _) =>
        {
            if (DataContext is TypingTrainerViewModel vm)
                vm.Dispose();
        };
    }
}
