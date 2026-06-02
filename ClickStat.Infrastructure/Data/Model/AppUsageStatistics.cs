using System.ComponentModel.DataAnnotations;

namespace ClickStat.Infrastructure.Data.Model;

public class AppUsageStatistics
{
    [Key]
    [MaxLength(260)]
    public string ExeName { get; set; } = "";
    public string AppName { get; set; } = "";
    public int KeyCount { get; set; }
    public int ClickCount { get; set; }
}
