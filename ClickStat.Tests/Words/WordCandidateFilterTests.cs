using ClickStat.Infrastructure.Data.Words;

namespace ClickStat.Tests.Words;

public class WordCandidateFilterTests
{
    [Theory]
    [InlineData("hello", true)]
    [InlineData("привет", true)]
    [InlineData("café", true)]
    [InlineData("niño", true)]
    [InlineData("straße", true)]
    [InlineData(null, false)]
    [InlineData("   ", false)]
    [InlineData("hello123", false)]
    [InlineData("hellooo", false)]
    public void IsWordCandidate_ReturnsExpectedResult(
        string? input, bool expected)
    {
        var actual = WordCandidateFilter.IsWordCandidate(input);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("café noir", true)]
    [InlineData("hola mundo", true)]
    [InlineData("one two three", true)]
    [InlineData("hello", false)]
    [InlineData("one two three four", false)]
    [InlineData("hello 123", false)]
    public void IsPhraseCandidate_ReturnsExpectedResult(
        string input, bool expected)
    {
        var actual = WordCandidateFilter.IsPhraseCandidate(input);

        Assert.Equal(expected, actual);
    }
    [Fact]
    public void IsWordCandidate_RejectsWordAboveMaximumLength()
    {
        var word = string.Concat(Enumerable.Repeat("ab", 60));

        Assert.True(WordCandidateFilter.IsWordCandidate(word));
        Assert.False(WordCandidateFilter.IsWordCandidate(word + "c"));
    }
}