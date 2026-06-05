using ClickStat.Infrastructure.Data.Model;
using Microsoft.EntityFrameworkCore;

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

    private readonly string _dbPath;

    public DataContext(string dbPath)
    {
        _dbPath = dbPath;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite($"Data Source={_dbPath}");
    }
}
