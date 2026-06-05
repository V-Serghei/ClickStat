using System.ComponentModel.DataAnnotations;

namespace ClickStat.Infrastructure.Data.Model;

public class MouseScrollStatistics
{
    [Key]
    public int Id { get; set; }
    public long ScrollUpNotches { get; set; }
    public long ScrollDownNotches { get; set; }
}
