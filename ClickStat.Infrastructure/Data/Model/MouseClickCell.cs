using System.ComponentModel.DataAnnotations;

namespace ClickStat.Infrastructure.Data.Model;

/// <summary>
/// Discretised click heatmap cell.
/// Screen is divided into a GridW × GridH grid; each click increments its cell.
/// </summary>
public class MouseClickCell
{
    [Key]
    public int CellId { get; set; } // GridX * GridHeight + GridY
    public int GridX { get; set; }
    public int GridY { get; set; }
    public int Count { get; set; }
}
