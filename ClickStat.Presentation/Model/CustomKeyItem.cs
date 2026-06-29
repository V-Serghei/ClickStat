namespace ClickStat.Presentation.Model;

public class CustomKeyItem
{
    public string KeyName  { get; set; } = "";
    public int    Count    { get; set; }

    public string FormattedCount => Count switch
    {
        >= 1_000_000 => $"{Count / 1_000_000.0:F1}M",
        >= 1_000     => $"{Count / 1_000.0:F1}K",
        _            => Count.ToString()
    };
}
