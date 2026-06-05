using System.IO;
using ClickStat.Infrastructure.Data.Context;
using ClickStat.Infrastructure.Data.Model;
using Microsoft.EntityFrameworkCore;

namespace ClickStat.Infrastructure.Data;

public class KeyGetDataClick
{
    private readonly string _dbPath;

    public KeyGetDataClick()
    {
        var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var folderPath    = Path.Combine(documentsPath, "KeyClick");
        Directory.CreateDirectory(folderPath);

        _dbPath = Path.Combine(folderPath, "key_statistics.db");

        using var context = new DataContext(_dbPath);
        context.Database.EnsureCreated();
    }

    public async Task<List<KeyStatistics>> GetKeyStatistics()
    {
        await using var context = new DataContext(_dbPath);
        return await context.KeyStatistics.ToListAsync();
    }

    public async Task<List<KeyStatistics>> GetKeyStatisticsByKeyCode(int keyCode)
    {
        await using var context = new DataContext(_dbPath);
        return await context.KeyStatistics
            .Where(k => k.KeyCode == keyCode)
            .ToListAsync();
    }

    public async Task<List<KeyStatistics>> GetKeyStatisticsByKeyName(string keyName)
    {
        await using var context = new DataContext(_dbPath);
        return await context.KeyStatistics
            .Where(k => k.KeyName.Equals(keyName, StringComparison.OrdinalIgnoreCase))
            .ToListAsync();
    }

    /// <summary>
    /// Returns the click count for a single day.
    /// NOTE: never writes — missing days return an empty list.
    /// </summary>
    public async Task<List<KeyStatisticsForTheDay>> GetKeyStatisticsForTheDay(DateTime date)
    {
        await using var context = new DataContext(_dbPath);
        var records = await context.KeyStatisticsForTheDay
            .Where(k => k.Date.Date == date.Date)
            .ToListAsync();

        // Merge duplicates if they exist (legacy cleanup only — no new writes for missing dates)
        if (records.Count > 1)
        {
            var primary = records[0];
            for (int i = 1; i < records.Count; i++)
                primary.ClickCount += records[i].ClickCount;

            context.KeyStatisticsForTheDay.RemoveRange(records.Skip(1));
            await context.SaveChangesAsync();

            return new List<KeyStatisticsForTheDay> { primary };
        }

        return records; // May be empty — callers should handle that
    }

    /// <summary>
    /// Single query for all days in [from, to]. Returns 0 for days with no records.
    /// Much faster than N sequential calls to GetKeyStatisticsForTheDay.
    /// </summary>
    public async Task<Dictionary<DateTime, int>> GetDailyClickCounts(DateTime from, DateTime to)
    {
        await using var context = new DataContext(_dbPath);
        var records = await context.KeyStatisticsForTheDay
            .Where(k => k.Date >= from.Date && k.Date <= to.Date)
            .ToListAsync();

        // Aggregate in memory in case there are legacy duplicates per date
        return records
            .GroupBy(r => r.Date.Date)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.ClickCount));
    }

    public async Task<int> GetKeyStatisticsForTheAllTime()
    {
        await using var context = new DataContext(_dbPath);
        return await context.KeyStatisticsForTheDay.SumAsync(k => k.ClickCount);
    }
}
