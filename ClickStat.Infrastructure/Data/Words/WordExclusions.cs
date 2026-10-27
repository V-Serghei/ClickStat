using System.IO;
using System.Text.Json;
using ClickStat.Infrastructure.Data.Model;

namespace ClickStat.Infrastructure.Data.Words;

public sealed class WordExclusions
{
    private readonly HashSet<string> _words;
    private readonly HashSet<string> _phrases;

    public string LanguageCode { get; }

    private WordExclusions(WordExclusionFile settings)
    {
        LanguageCode = InputLanguageCode.Normalize(settings.Language);
        _words = CreateSet(settings.Words);
        _phrases = CreateSet(settings.Phrases);
    }

    public static WordExclusions Load(string filePath)
    {
        string json = File.ReadAllText(filePath);
        string expectedLanguage =
            Path.GetFileNameWithoutExtension(filePath);

        return FromJson(json, expectedLanguage);
    }

    public static WordExclusions FromJson(
        string json,
        string expectedLanguage)
    {
        var settings = JsonSerializer.Deserialize<WordExclusionFile>(
            json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

        if (settings is null)
            throw new InvalidDataException("Файл исключений пуст.");

        if (settings.Version != 1)
            throw new InvalidDataException(
                $"Неизвестная версия формата: {settings.Version}.");

        string code = InputLanguageCode.Normalize(settings.Language);

        if (!string.Equals(
                settings.Language, code,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                code, expectedLanguage,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Язык внутри файла не совпадает с его именем.");
        }

        if (settings.Words is null || settings.Phrases is null)
        {
            throw new InvalidDataException(
                "Списки words и phrases должны быть массивами.");
        }

        return new WordExclusions(settings);
    }

    public bool IsWordExcluded(string word)
    {
        return _words.Contains(word.Trim());
    }

    public bool IsPhraseExcluded(string phrase)
    {
        return _phrases.Contains(phrase.Trim());
    }

    private static HashSet<string> CreateSet(string[] values)
    {
        return new HashSet<string>(
            values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim()),
            StringComparer.OrdinalIgnoreCase);
    }
}