using System.ComponentModel.DataAnnotations;

namespace ClickStat.Infrastructure.Data.Model;

public class KeyStatistics
{
    [Key]
    public int KeyCode { get; set; }
    public string KeyName { get; set; }
    public int Count { get; set; }
}