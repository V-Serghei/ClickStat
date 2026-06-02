using System.ComponentModel.DataAnnotations;

namespace ClickStat.Infrastructure.Data.Model;

public class MouseDistance
{
    [Key]
    public int Id { get; set; }
    /// <summary>Total cursor travel in 0.01-mm units at assumed 96 DPI.</summary>
    public long TotalUnits { get; set; }
}
