using ClickStat.Infrastructure.Data.Model;
using Microsoft.EntityFrameworkCore;

namespace ClickStat.Infrastructure.Data.Context;

public class DataContext : DbContext
{
    public DbSet<KeyStatistics> KeyStatistics { get; set; }
    public DbSet<KeyStatisticsForTheDay> KeyStatisticsForTheDay { get; set; }
    
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