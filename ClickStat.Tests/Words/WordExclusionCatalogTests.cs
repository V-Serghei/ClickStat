using System.IO;
using System.Text.Json;
using ClickStat.Infrastructure.Data.Model;
using ClickStat.Infrastructure.Data.Words;

namespace ClickStat.Tests.Words;

public sealed class WordExclusionCatalogTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"ClickStat-tests-{Guid.NewGuid():N}");

    private readonly string _russianFile;

    public WordExclusionCatalogTests()
    {
        Directory.CreateDirectory(_directory);
        _russianFile = Path.Combine(_directory, "ru.json");
    }

    [Fact]
    public void Reload_ReplacesRulesOnlyAfterReload()
    {
        WriteRussianRules("по");

        var catalog = new WordExclusionCatalog(_directory);

        Assert.Null(catalog.Reload());
        Assert.True(catalog.IsWordExcluded("по", "RU"));
        Assert.False(catalog.IsWordExcluded("по", "EN"));

        WriteRussianRules("куда");

        // Файл изменён, но в памяти пока прежние правила.
        Assert.True(catalog.IsWordExcluded("по", "RU"));

        Assert.Null(catalog.Reload());

        Assert.False(catalog.IsWordExcluded("по", "RU"));
        Assert.True(catalog.IsWordExcluded("куда", "RU"));
    }

    [Fact]
    public void Reload_BrokenJson_KeepsPreviousRules()
    {
        WriteRussianRules("по");

        var catalog = new WordExclusionCatalog(_directory);
        Assert.Null(catalog.Reload());

        File.WriteAllText(_russianFile, "{broken json");

        var error = catalog.Reload();

        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.True(catalog.IsWordExcluded("по", "RU"));
        Assert.True(catalog.IsPhraseExcluded("ну да", "RU"));
    }

    private void WriteRussianRules(params string[] words)
    {
        var settings = new WordExclusionFile
        {
            Version = 1,
            Language = "RU",
            Words = words,
            Phrases = ["ну да"]
        };

        File.WriteAllText(
            _russianFile,
            JsonSerializer.Serialize(settings));
    }

    public void Dispose()
    {
        foreach (string file in Directory.EnumerateFiles(_directory))
            File.Delete(file);

        Directory.Delete(_directory);
    }
}