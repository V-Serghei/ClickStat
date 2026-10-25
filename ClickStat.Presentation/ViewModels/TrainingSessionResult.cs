namespace ClickStat.Presentation.ViewModels;

public sealed record TrainingSessionResult(string Language, int TextLength, int CorrectChars,
    int Errors, double Accuracy, double Cpm, double Wpm, int DurationSeconds,
    bool IsSaved, string SaveError)
{
    public string ResultHint => IsSaved
        ? Services.LocalizationService.Instance["Training.ResultHint"]
        : Services.LocalizationService.Instance["Training.ResultSaveFailed"] + " " + SaveError;
}
