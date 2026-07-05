using System.Windows;
using ClickStat.Infrastructure.Data;
using ClickStat.Presentation.ViewModels;
using ClickStat.Presentation.Views;

namespace ClickStat.Presentation.Services;

public sealed class TypingTrainerWindowService : ITypingTrainerWindowService
{
    private readonly LiveEventBus _liveBus;
    private readonly TypingTrainingProcessor _processor;
    private TypingTrainerWindow? _window;

    public TypingTrainerWindowService(LiveEventBus liveBus, TypingTrainingProcessor processor)
    {
        _liveBus = liveBus;
        _processor = processor;
    }

    public event EventHandler? TrainerClosed;

    public void Show()
    {
        if (_window is { IsVisible: true })
        {
            _window.Activate();
            return;
        }

        var vm = new TypingTrainerViewModel(_liveBus, _processor);
        var window = new TypingTrainerWindow
        {
            Owner = Application.Current.MainWindow,
            DataContext = vm
        };

        window.Closed += (_, _) =>
        {
            _window = null;
            TrainerClosed?.Invoke(this, EventArgs.Empty);
        };

        _window = window;
        window.Show();
    }
}
