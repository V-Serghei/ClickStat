using ClickStat.Infrastructure.Data.Words;

namespace ClickStat.Infrastructure.Data.Model;

public sealed record CapturedWord(string Text, string LanguageCode)
{
    public static CapturedWord Create(string text, string? languageCode)
    {
        return new CapturedWord(
            text,
            InputLanguageCode.Normalize(languageCode));
    }
}
