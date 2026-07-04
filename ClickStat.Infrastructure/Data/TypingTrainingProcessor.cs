using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;

namespace ClickStat.Infrastructure.Data;

public sealed record TypingTrainingResult(
    int Id,
    string Language,
    int TextLength,
    int CorrectChars,
    int Errors,
    double Accuracy,
    double Cpm,
    double Wpm,
    int DurationSeconds,
    string CompletedAt);

public sealed record TypingTrainingSummary(
    int Sessions,
    double BestWpm,
    double AverageWpm,
    double AverageAccuracy,
    int TotalCorrectChars,
    int TotalErrors);

public sealed class TypingTrainingProcessor
{
    private const int RecentLimit = 60;
    private readonly string _connectionString;
    private readonly object _schemaGate = new();
    private bool _schemaReady;

    public TypingTrainingProcessor()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var folder = Path.Combine(docs, "KeyClick");
        Directory.CreateDirectory(folder);
        var dbPath = Path.Combine(folder, "key_statistics.db");
        _connectionString = BuildConnectionString(dbPath);
    }

    public async Task SaveResultAsync(
        string language,
        int textLength,
        int correctChars,
        int errors,
        double accuracy,
        double cpm,
        double wpm,
        int durationSeconds)
    {
        EnsureTable();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO TypingTrainingResults
                (Language, TextLength, CorrectChars, Errors, Accuracy, Cpm, Wpm, DurationSeconds, CompletedAt)
            VALUES
                ($language, $textLength, $correctChars, $errors, $accuracy, $cpm, $wpm, $durationSeconds, $completedAt);
            """;
        command.Parameters.AddWithValue("$language", language);
        command.Parameters.AddWithValue("$textLength", textLength);
        command.Parameters.AddWithValue("$correctChars", correctChars);
        command.Parameters.AddWithValue("$errors", errors);
        command.Parameters.AddWithValue("$accuracy", accuracy);
        command.Parameters.AddWithValue("$cpm", cpm);
        command.Parameters.AddWithValue("$wpm", wpm);
        command.Parameters.AddWithValue("$durationSeconds", durationSeconds);
        command.Parameters.AddWithValue("$completedAt", DateTimeOffset.Now.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<TypingTrainingResult>> GetRecentAsync()
    {
        EnsureTable();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Language, TextLength, CorrectChars, Errors, Accuracy, Cpm, Wpm, DurationSeconds, CompletedAt
            FROM TypingTrainingResults
            ORDER BY Id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", RecentLimit);

        var result = new List<TypingTrainingResult>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(ReadResult(reader));

        return result;
    }

    public async Task<IReadOnlyDictionary<string, TypingTrainingSummary>> GetSummaryByLanguageAsync()
    {
        EnsureTable();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Language,
                   COUNT(*) AS Sessions,
                   IFNULL(MAX(Wpm), 0),
                   IFNULL(AVG(Wpm), 0),
                   IFNULL(AVG(Accuracy), 0),
                   IFNULL(SUM(CorrectChars), 0),
                   IFNULL(SUM(Errors), 0)
            FROM TypingTrainingResults
            GROUP BY Language
            ORDER BY Sessions DESC, Language ASC;
            """;

        var result = new Dictionary<string, TypingTrainingSummary>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result[reader.GetString(0)] = new TypingTrainingSummary(
                reader.GetInt32(1),
                Math.Round(reader.GetDouble(2), 1),
                Math.Round(reader.GetDouble(3), 1),
                Math.Round(reader.GetDouble(4), 1),
                reader.GetInt32(5),
                reader.GetInt32(6));
        }

        return result;
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

                CREATE TABLE IF NOT EXISTS TypingTrainingResults (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Language TEXT NOT NULL,
                    TextLength INTEGER NOT NULL,
                    CorrectChars INTEGER NOT NULL,
                    Errors INTEGER NOT NULL,
                    Accuracy REAL NOT NULL,
                    Cpm REAL NOT NULL,
                    Wpm REAL NOT NULL,
                    DurationSeconds INTEGER NOT NULL,
                    CompletedAt TEXT NOT NULL
                );

                CREATE INDEX IF NOT EXISTS IX_TypingTrainingResults_Language
                ON TypingTrainingResults (Language);

                CREATE INDEX IF NOT EXISTS IX_TypingTrainingResults_CompletedAt
                ON TypingTrainingResults (CompletedAt);
                """;
            command.ExecuteNonQuery();
            _schemaReady = true;
        }
    }

    private static TypingTrainingResult ReadResult(SqliteDataReader reader) =>
        new(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.GetInt32(2),
            reader.GetInt32(3),
            reader.GetInt32(4),
            Math.Round(reader.GetDouble(5), 1),
            Math.Round(reader.GetDouble(6), 1),
            Math.Round(reader.GetDouble(7), 1),
            reader.GetInt32(8),
            FormatDate(reader.GetString(9)));

    private static string FormatDate(string value)
    {
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return date.LocalDateTime.ToString("dd.MM.yyyy HH:mm", CultureInfo.CurrentCulture);

        return value;
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
}
