using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ClickStat.Infrastructure.Data.Model;

public class HourlyActivity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)] // explicit ID, not autoincrement
    public int Id { get; set; }        // DayOfWeek * 24 + Hour
    public int DayOfWeek { get; set; } // 0=Sun … 6=Sat
    public int Hour { get; set; }      // 0-23
    public int Count { get; set; }
}
