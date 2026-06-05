namespace ClickStat.Presentation.Model;

public class CustomKeyItem
{
    public string KeyName  { get; set; } = "";
    public int    Count    { get; set; }

    public string FormattedCount => Count switch
    {
        >= 1_000_000 => $"{Count / 1_000_000.0:F1}М",
        >= 1_000     => $"{Count / 1_000.0:F1}к",
        _            => Count.ToString()
    };
}
