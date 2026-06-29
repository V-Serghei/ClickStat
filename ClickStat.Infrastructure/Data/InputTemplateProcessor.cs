using System.IO;
using Microsoft.Data.Sqlite;

namespace ClickStat.Infrastructure.Data;

public sealed record InputTemplateEntry(int Id, string Title, string Text, string CreatedAt);

public sealed class InputTemplateProcessor
{
    private const int SearchLimit = 80;
    private readonly string _dbPath;
    private readonly string _connectionString;
    private readonly object _schemaGate = new();
    private bool _schemaReady;

    public InputTemplateProcessor()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var folder = Path.Combine(docs, "KeyClick");
        Directory.CreateDirectory(folder);
        _dbPath = Path.Combine(folder, "key_statistics.db");
        _connectionString = BuildConnectionString(_dbPath);

    }

    public async Task SaveAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        EnsureTable();
        await using var connection = new SqliteConnection(_connectionString);
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
        EnsureTable();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();

        if (string.IsNullOrWhiteSpace(query))
        {
            command.CommandText = """
                SELECT Id, Title,
                       CASE
                           WHEN length(Text) <= 220 THEN Text
                           ELSE substr(Text, 1, 220) || '...'
                       END AS Preview,
                       CreatedAt
                FROM InputTemplates
                ORDER BY Id DESC
                LIMIT $limit;
                """;
        }
        else
        {
            command.CommandText = """
                SELECT Id, Title,
                       CASE
                           WHEN length(Text) <= 220 THEN Text
                           ELSE substr(Text, 1, 220) || '...'
                       END AS Preview,
                       CreatedAt
                FROM InputTemplates
                WHERE Title LIKE $query OR Text LIKE $query
                ORDER BY Id DESC
                LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$query", $"%{query.Trim()}%");
        }

        command.Parameters.AddWithValue("$limit", SearchLimit);

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

    public async Task<string?> GetTextAsync(int id)
    {
        EnsureTable();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Text FROM InputTemplates WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        var value = await command.ExecuteScalarAsync();
        return value == null || value == DBNull.Value ? null : Convert.ToString(value);
    }

    public async Task DeleteAsync(int id)
    {
        EnsureTable();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM InputTemplates WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync();
    }

    private void EnsureTable()
    {
        lock (_schemaGate)
        {
            if (_schemaReady)
                return;

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA busy_timeout = 5000;
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;

            CREATE TABLE IF NOT EXISTS InputTemplates (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Title TEXT NOT NULL,
                Text TEXT NOT NULL,
                CreatedAt TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_InputTemplates_CreatedAt
            ON InputTemplates (CreatedAt);
            """;
        command.ExecuteNonQuery();
            _schemaReady = true;
        }
    }

    private static string BuildConnectionString(string dbPath)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            DefaultTimeout = 5
        };

        return builder.ToString();
    }

    private static string BuildTitle(string text)
    {
        var normalized = string.Join(" ", text.Split(default(string[]), StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length == 0)
            return "Untitled";

        return normalized.Length <= 48 ? normalized : normalized[..48] + "...";
    }
}
