using System.IO;
using Microsoft.Data.Sqlite;

namespace ClickStat.Infrastructure.Data;

public sealed record InputTemplateEntry(int Id, string Title, string Text, string CreatedAt);

public sealed class InputTemplateProcessor
{
    private readonly string _dbPath;

    public InputTemplateProcessor()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var folder = Path.Combine(docs, "KeyClick");
        Directory.CreateDirectory(folder);
        _dbPath = Path.Combine(folder, "key_statistics.db");

        EnsureTable();
    }

    public async Task SaveAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        await using var connection = new SqliteConnection($"Data Source={_dbPath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO InputTemplates (Title, Text, CreatedAt)
            VALUES ($title, $text, $createdAt);
            """;
        command.Parameters.AddWithValue("$title", BuildTitle(text));
        command.Parameters.AddWithValue("$text", text);
        command.Parameters.AddWithValue("$createdAt", DateTimeOffset.Now.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<InputTemplateEntry>> SearchAsync(string query = "")
    {
        await using var connection = new SqliteConnection($"Data Source={_dbPath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();

        if (string.IsNullOrWhiteSpace(query))
        {
            command.CommandText = """
                SELECT Id, Title, Text, CreatedAt
                FROM InputTemplates
                ORDER BY Id DESC;
                """;
        }
        else
        {
            command.CommandText = """
                SELECT Id, Title, Text, CreatedAt
                FROM InputTemplates
                WHERE Title LIKE $query OR Text LIKE $query
                ORDER BY Id DESC;
                """;
            command.Parameters.AddWithValue("$query", $"%{query.Trim()}%");
        }

        var result = new List<InputTemplateEntry>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new InputTemplateEntry(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3)));
        }

        return result;
    }

    public async Task DeleteAsync(int id)
    {
        await using var connection = new SqliteConnection($"Data Source={_dbPath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM InputTemplates WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync();
    }

    private void EnsureTable()
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS InputTemplates (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Title TEXT NOT NULL,
                Text TEXT NOT NULL,
                CreatedAt TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    private static string BuildTitle(string text)
    {
        var normalized = string.Join(" ", text.Split(default(string[]), StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length == 0)
            return "Untitled";

        return normalized.Length <= 48 ? normalized : normalized[..48] + "...";
    }
}
