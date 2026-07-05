namespace ClickStat.Presentation.Model;

public enum TypingTrainingInputResult
{
    Correct,
    Incorrect
}

public sealed record TypingTrainingPreview(string TypedPrefix, string CurrentCharacter, string UpcomingText);

public sealed class TypingTrainingSession
{
    public string SourceText { get; set; } = "";
    public int Position { get; private set; }
    public int CorrectChars { get; private set; }
    public int Errors { get; private set; }

    public int TotalCharacters => SourceText.Length;
    public int RemainingCharacters => Math.Max(0, TotalCharacters - Position);
    public double ProgressPercent => TotalCharacters == 0 ? 0 : Math.Round(Position * 100.0 / TotalCharacters, 1);
    public double Accuracy => CorrectChars + Errors == 0 ? 100 : Math.Round(CorrectChars * 100.0 / (CorrectChars + Errors), 1);
    public string DetectedLanguage => DetectLanguage(SourceText);
    public bool IsComplete => Position >= SourceText.Length;

    public void ResetProgress()
    {
        Position = 0;
        CorrectChars = 0;
        Errors = 0;
    }

    public TypingTrainingInputResult Accept(char actual, out char expected)
    {
        expected = Position < SourceText.Length ? SourceText[Position] : '\0';
        if (actual == expected)
        {
            CorrectChars++;
            Position++;
            return TypingTrainingInputResult.Correct;
        }

        Errors++;
        return TypingTrainingInputResult.Incorrect;
    }

    public TypingTrainingPreview GetPreview(int radius)
    {
        var safePosition = Math.Clamp(Position, 0, SourceText.Length);
        var prefixStart = Math.Max(0, safePosition - radius);
        var typedPrefix = SourceText.Substring(prefixStart, safePosition - prefixStart);

        var currentCharacter = safePosition < SourceText.Length
            ? DisplayChar(SourceText[safePosition])
            : "";

        var upcomingStart = Math.Min(SourceText.Length, safePosition + 1);
        var upcomingLength = Math.Min(radius, SourceText.Length - upcomingStart);
        var upcomingText = upcomingLength > 0 ? SourceText.Substring(upcomingStart, upcomingLength) : "";

        return new TypingTrainingPreview(typedPrefix, currentCharacter, upcomingText);
    }

    public static string DisplayChar(char ch) => ch switch
    {
        '\n' => "Enter",
        '\t' => "Tab",
        ' ' => "Space",
        _ => ch.ToString()
    };

    private static string DetectLanguage(string text)
    {
        var cyrillic = text.Count(c => c >= '\u0400' && c <= '\u04FF');
        var latin = text.Count(c => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'));

        if (cyrillic == 0 && latin == 0) return "Other";
        if (cyrillic > 0 && latin > 0) return "Mixed";
        return cyrillic > latin ? "Russian" : "English";
    }
}
