namespace ClickStat.Infrastructure.Data.Words;

public static class WordCandidateFilter
{
    
    
    public static bool IsWordCandidate(string? word)
    {
        if (string.IsNullOrWhiteSpace(word))
        {
            return false;
        }
        
        var normalized = word.Trim().ToLowerInvariant();
        
        if (normalized.Length < 2 || normalized.Length > 120)
        {
            return false;
        }
        
        var previousChar = '\0';
        var repeatCount = 0;
        
        foreach (var c in normalized)
        {
            if (!char.IsLetter(c))
                return false;

            repeatCount = c == previousChar ? repeatCount + 1 : 1;

            if (repeatCount > 2)
                return false;

            previousChar = c;
        }
        return true;
        
    }
    
    public static bool IsPhraseCandidate(string? phrase)
    {
        if (string.IsNullOrWhiteSpace(phrase))
        {
            return false;
        }
        
        var words = phrase.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries);

        return words.Length is 2 or 3 &&
               words.All(IsWordCandidate);
    }
}