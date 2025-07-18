namespace ClickStat.Core.Models;

public class MouseAction
{
    public MouseActionType ActionType { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public DateTime Timestamp { get; set; }
}