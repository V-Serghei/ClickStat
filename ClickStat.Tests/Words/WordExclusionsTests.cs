using ClickStat.Infrastructure.Data.Words;

namespace ClickStat.Tests.Words;

public class WordExclusionsTests
{
    [Fact]
    public void FromJson_LoadsWordsAndPhrasesIgnoringCase()
    {
        const string json = """
                            {
                              "version": 1,
                              "language": "RU",
                              "words": ["по", "куда"],
                              "phrases": ["ну да"]
                            }
                            """;

        var exclusions = WordExclusions.FromJson(json, "ru");

        Assert.True(exclusions.IsWordExcluded("ПО"));
        Assert.True(exclusions.IsPhraseExcluded("Ну Да"));

        Assert.False(exclusions.IsWordExcluded("привет"));
        Assert.False(exclusions.IsPhraseExcluded("ну привет"));
    }

    [Fact]
    public void FromJson_RejectsWrongLanguage()
    {
        const string json = """
                            {
                              "version": 1,
                              "language": "EN",
                              "words": [],
                              "phrases": []
                            }
                            """;

        Assert.Throws<InvalidDataException>(() =>
            WordExclusions.FromJson(json, "ru"));
    }
}