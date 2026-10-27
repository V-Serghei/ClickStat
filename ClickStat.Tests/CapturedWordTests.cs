using ClickStat.Infrastructure.Data.Model;

namespace ClickStat.Tests;

public class CapturedWordTests
{
    [Theory]
    [InlineData("hello", "en-US", "EN")]
    [InlineData("bonjour", "fr-FR", "FR")]
    [InlineData("hola", "es-ES", "ES")]
    [InlineData("Привет", "ru-RU", "RU")]
    [InlineData("bonjour", "en-US", "EN")]
    [InlineData("bonjour", null, "UND")]
    public void Create_PreservesInputLanguage(
        string text,
        string? layoutCode,
        string expected)
    {
        var actual = CapturedWord.Create(text, layoutCode);

        Assert.Equal(expected, actual.LanguageCode);
    }
}
