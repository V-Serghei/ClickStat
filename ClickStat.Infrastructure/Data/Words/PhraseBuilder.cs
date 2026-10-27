using ClickStat.Infrastructure.Data.Model;

namespace ClickStat.Infrastructure.Data.Words;

public sealed class PhraseBuilder
{
    private readonly Queue<CapturedWord> _recentWords = new(2);

    public IReadOnlyList<CapturedPhrase> Add(CapturedWord word)
    {
        if (_recentWords.Count > 0 &&
            _recentWords.Peek().LanguageCode != word.LanguageCode)
        {
            Clear();
        }

        var previous = _recentWords.ToArray();
        var phrases = new List<CapturedPhrase>(2);

        if (previous.Length >= 1)
        {
            phrases.Add(new CapturedPhrase(
                $"{previous[^1].Text} {word.Text}",
                word.LanguageCode));
        }

        if (previous.Length == 2)
        {
            phrases.Add(new CapturedPhrase(
                $"{previous[0].Text} {previous[1].Text} {word.Text}",
                word.LanguageCode));
        }

        _recentWords.Enqueue(word);

        while (_recentWords.Count > 2)
            _recentWords.Dequeue();

        return phrases;
    }

    public void Clear()
    {
        _recentWords.Clear();
    }
}