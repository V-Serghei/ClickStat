using ClickStat.Infrastructure.Data.Model;
using ClickStat.Infrastructure.Data.Words;

namespace ClickStat.Tests.Words;

public class PhraseBuilderTests
{
    [Fact]
    public void Add_TwoWords_CreatesPhraseWithTheirLanguage()
    {
        var builder = new PhraseBuilder();

        builder.Add(CapturedWord.Create("café", "FR"));
        var result = builder.Add(CapturedWord.Create("noir", "FR"));

        var phrase = Assert.Single(result);

        Assert.Equal(new CapturedPhrase("café noir", "FR"), phrase);
    }

    [Fact]
    public void Add_ThirdWord_CreatesTwoAndThreeWordPhrases()
    {
        var builder = new PhraseBuilder();

        builder.Add(CapturedWord.Create("one", "EN"));
        builder.Add(CapturedWord.Create("two", "EN"));
        var result = builder.Add(CapturedWord.Create("three", "EN"));

        Assert.Equal(
            new[] { "two three", "one two three" },
            result.Select(phrase => phrase.Text).ToArray());
    }

    [Fact]
    public void Add_LanguageChanges_DoesNotJoinDifferentLanguages()
    {
        var builder = new PhraseBuilder();

        builder.Add(CapturedWord.Create("hello", "EN"));
        var result = builder.Add(CapturedWord.Create("bonjour", "FR"));

        Assert.Empty(result);
    }
}