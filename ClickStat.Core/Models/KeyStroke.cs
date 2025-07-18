namespace ClickStat.Core.Models;

public class KeyStroke
{
    public int KeyCode { get; set; }
    public string KeyName { get; set; }
    public DateTime Timestamp { get; set; }
    public bool IsModifier { get; set; }
}