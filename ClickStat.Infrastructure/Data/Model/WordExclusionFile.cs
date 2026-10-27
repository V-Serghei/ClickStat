namespace ClickStat.Infrastructure.Data.Model;

public sealed class WordExclusionFile
{
    public int Version { get; set; }

    public string Language { get; set; } = "";

    public string[] Words { get; set; } = [];

    public string[] Phrases { get; set; } = [];
}