using System.ComponentModel.DataAnnotations;

namespace ClickStat.Infrastructure.Data.Model;

public class WordPhrase
{
    [Key]
    [MaxLength(240)]
    public string Phrase { get; set; } = ""; // "word1 word2" or "word1 word2 word3"
    public int Count { get; set; }
}
