using System.ComponentModel.DataAnnotations;

namespace ClickStat.Infrastructure.Data.Model;

public class MouseStatistics
{
    [Key]
    public int ButtonCode { get; set; }
    public string ButtonName { get; set; }
    public long Count { get; set; }
    public bool IsRegistered { get; set; }
}