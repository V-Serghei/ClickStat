using ClickStat.Infrastructure.Data.Model;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;

namespace ClickStat.Infrastructure.Data.Context;

public class DataContext : DbContext
{
    // Keyboard
    public DbSet<KeyStatistics>        KeyStatistics        { get; set; }
    public DbSet<KeyStatisticsForTheDay> KeyStatisticsForTheDay { get; set; }
    public DbSet<KeyBigram>            KeyBigrams           { get; set; }

    // Words
    public DbSet<WordStatistics>       WordStatistics       { get; set; }
    public DbSet<WordPhrase>           WordPhrases          { get; set; }

    // Mouse
    public DbSet<MouseStatistics>      MouseStatistics      { get; set; }
    public DbSet<MouseScrollStatistics> MouseScrollStatistics { get; set; }
    public DbSet<MouseClickCell>       MouseClickCells      { get; set; }
    public DbSet<MouseDistance>        MouseDistances       { get; set; }

    // Activity
    public DbSet<HourlyActivity>       HourlyActivities     { get; set; }

    // Apps
    public DbSet<AppUsageStatistics>   AppUsageStatistics   { get; set; }

    // Saved input snippets
    public DbSet<InputTemplate>         InputTemplates       { get; set; }

    private readonly string _dbPath;
    private static readonly ConcurrentDictionary<string, byte> ConfiguredDatabases = new(StringComparer.OrdinalIgnoreCase);

    public DataContext(string dbPath)
    {
        _dbPath = dbPath;
        ConfigureDatabaseOnce(dbPath);
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite(BuildConnectionString(_dbPath), options => options.CommandTimeout(5));
    }

    private static void ConfigureDatabaseOnce(string dbPath)
    {
        if (!ConfiguredDatabases.TryAdd(dbPath, 0))
            return;

        using var connection = new SqliteConnection(BuildConnectionString(dbPath));
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA busy_timeout = 5000;
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            """;
        command.ExecuteNonQuery();
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
