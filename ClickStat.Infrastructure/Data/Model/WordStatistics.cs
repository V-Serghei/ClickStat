using System;
using System.ComponentModel.DataAnnotations;

namespace ClickStat.Infrastructure.Data.Model;

public class WordStatistics
{
    [MaxLength(120)]
    public string Word { get; set; } = "";
    public int Count { get; set; }
    public DateTime LastTyped { get; set; }
    public bool IsKnown { get; set; } // found in common-words dictionary
    [MaxLength(3)]
    public string LanguageCode { get; set; } = "UND";
}
