using System.ComponentModel.DataAnnotations;

namespace ClickStat.Infrastructure.Data.Model;

public class KeyBigram
{
    [Key]
    [MaxLength(80)]
    public string Pair { get; set; } = ""; // "A|B" — two consecutive key names
    public int Count { get; set; }
}
