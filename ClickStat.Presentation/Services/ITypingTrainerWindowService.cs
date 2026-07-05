namespace ClickStat.Presentation.Services;

public interface ITypingTrainerWindowService
{
    event EventHandler? TrainerClosed;

    void Show();
}
