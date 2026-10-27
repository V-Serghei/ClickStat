using System.IO;
using System.Text.Json;

namespace ClickStat.Infrastructure.Data.Words;

public sealed class WordExclusionCatalog
{
    private readonly string _directory;
    private readonly object _gate = new();

    private Dictionary<string, WordExclusions> _languages =
        new(StringComparer.OrdinalIgnoreCase);

    public WordExclusionCatalog(string directory)
    {
        _directory = directory;
    }

    public string? Reload()
    {
        string source = _directory;

        try
        {
            var loaded = new Dictionary<string, WordExclusions>(
                StringComparer.OrdinalIgnoreCase);

            if (Directory.Exists(_directory))
            {
                foreach (string file in Directory.EnumerateFiles(
                             _directory, "*.json"))
                {
                    source = Path.GetFileName(file);
                    var exclusions = WordExclusions.Load(file);

                    if (!loaded.TryAdd(
                            exclusions.LanguageCode, exclusions))
                    {
                        throw new InvalidDataException(
                            $"Повторяется язык {exclusions.LanguageCode}.");
                    }
                }
            }

            lock (_gate)
            {
                _languages = loaded;
            }

            return null;
        }
        catch (Exception ex) when (
            ex is IOException ||
            ex is UnauthorizedAccessException ||
            ex is InvalidDataException ||
            ex is JsonException)
        {
            return $"Ошибка исключений ({source}): {ex.Message}";
        }
    }

    public bool IsWordExcluded(string text, string languageCode)
    {
        lock (_gate)
        {
            return _languages.TryGetValue(languageCode, out var list) &&
                   list.IsWordExcluded(text);
        }
    }

    public bool IsPhraseExcluded(string text, string languageCode)
    {
        lock (_gate)
        {
            return _languages.TryGetValue(languageCode, out var list) &&
                   list.IsPhraseExcluded(text);
        }
    }
}