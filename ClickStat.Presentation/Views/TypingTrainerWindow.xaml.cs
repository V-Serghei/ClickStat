using System.Windows;
using ClickStat.Presentation.ViewModels;

namespace ClickStat.Presentation.Views;

public partial class TypingTrainerWindow : Window
{
    public TypingTrainerWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, args) =>
        {
            if (args.OldValue is TypingTrainerViewModel oldVm)
                oldVm.SessionFinished -= ShowSessionResult;
            if (args.NewValue is TypingTrainerViewModel newVm)
                newVm.SessionFinished += ShowSessionResult;
        };
        Closed += (_, _) =>
        {
            if (DataContext is TypingTrainerViewModel vm)
            {
                vm.SessionFinished -= ShowSessionResult;
                vm.Dispose();
            }
        };
    }

    private void ShowSessionResult(TrainingSessionResult result)
    {
        new TrainingResultWindow { Owner = this, DataContext = result }.ShowDialog();
    }
}
