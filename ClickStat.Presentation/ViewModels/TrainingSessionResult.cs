namespace ClickStat.Presentation.ViewModels;

public sealed record TrainingSessionResult(string Language, int TextLength, int CorrectChars,
    int Errors, double Accuracy, double Cpm, double Wpm, int DurationSeconds);
