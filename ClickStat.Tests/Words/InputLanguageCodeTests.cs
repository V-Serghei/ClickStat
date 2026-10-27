using ClickStat.Infrastructure.Data.Words;

namespace ClickStat.Tests.Words;

public class InputLanguageCodeTests
{
    
    [Fact]
    public void Normalize_ReturnsUnknown_ForNullInput()
    {
        string? input = null;
        const string expected = "UND";
        
        var actual = InputLanguageCode.Normalize(input);
        
        Assert.Equal(expected, actual);
    }
    
    [Fact]
    public void Normalize_InvalidCode_ReturnsUnknown()
    {
        var actual = InputLanguageCode.Normalize("12");
        
        Assert.Equal("UND", actual);
    }

    [Fact]
    public void Normalize_ValidCode_ReturnsNormalizedCode()
    {
        var actual = InputLanguageCode.Normalize("fr-FR");
        
        Assert.Equal("FR", actual);
    }
}