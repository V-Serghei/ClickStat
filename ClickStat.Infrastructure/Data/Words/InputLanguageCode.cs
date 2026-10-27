namespace ClickStat.Infrastructure.Data.Words;

public static class InputLanguageCode
{
    private const  string Unknown = "UND";
    
    
    public static string Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return Unknown;
        
        var normalized = input.Trim().ToUpperInvariant();
        var separatorIndex = normalized.IndexOfAny(['-', '_']);
        var language = separatorIndex >= 0
            ? normalized[..separatorIndex]
            : normalized;
        if (language.Length is < 2 or > 3 || language.Any(c => c is < 'A' or > 'Z'))
            return Unknown;

        return language;
    }
}