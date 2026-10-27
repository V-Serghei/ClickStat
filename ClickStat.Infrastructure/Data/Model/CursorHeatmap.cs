namespace ClickStat.Infrastructure.Data.Model;

public sealed record CursorHeatmapCell(int GridX, int GridY, long Seconds, long Visits);

public sealed record CursorHeatmap(int Left, int Top, int Width, int Height,
    int GridWidth, int GridHeight, IReadOnlyList<CursorHeatmapCell> Cells,
    IReadOnlyList<CursorHeatmapCell> LegacyCells)
{
    public long TotalSeconds => Cells.Sum(cell => cell.Seconds) + LegacyCells.Sum(cell => cell.Seconds);
    public long TotalVisits => Cells.Sum(cell => cell.Visits) + LegacyCells.Sum(cell => cell.Visits);
}
