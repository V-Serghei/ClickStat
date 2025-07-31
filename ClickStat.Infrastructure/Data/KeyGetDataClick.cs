using System.IO;
using ClickStat.Infrastructure.Data.Context;
using ClickStat.Infrastructure.Data.Model;
using Microsoft.EntityFrameworkCore;

namespace ClickStat.Infrastructure.Data;

public class KeyGetDataClick
{
    private readonly DataContext _context;
    private readonly string _dbPath;


    public KeyGetDataClick()
    {
        var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var folderPath = Path.Combine(documentsPath, "KeyClick");
        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
            Console.WriteLine($"Folder Create: {folderPath}");
        }

        _dbPath = Path.Combine(folderPath, "key_statistics.db");
        _context = new DataContext(_dbPath);
        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
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

    public async Task<List<KeyStatisticsForTheDay>> GetKeyStatisticsForTheDay(DateTime date)
    {
        
        await using var context = new DataContext(_dbPath);

        var keyStatisticsForTheDay = await context.KeyStatisticsForTheDay
            .Where(k => k.Date.Date == date.Date)
            .ToListAsync();

        if (keyStatisticsForTheDay.Count > 1)
        {
            var primaryRecord = keyStatisticsForTheDay[0];
            for (int i = 1; i < keyStatisticsForTheDay.Count; i++)
            {
                primaryRecord.ClickCount += keyStatisticsForTheDay[i].ClickCount;
            }

            var duplicates = keyStatisticsForTheDay.Skip(1).ToList();
            context.KeyStatisticsForTheDay.RemoveRange(duplicates);

            await context.SaveChangesAsync();
        }
        else if (keyStatisticsForTheDay.Count == 0)
        {
            var newRecord = new KeyStatisticsForTheDay
            {
                DayId = Guid.NewGuid(),
                Date = date.Date,
                ClickCount = 0
            };
            context.KeyStatisticsForTheDay.Add(newRecord);
            await context.SaveChangesAsync();
            keyStatisticsForTheDay.Add(newRecord);
        }
        
        return keyStatisticsForTheDay;
        
        
    }


public async Task<int> GetKeyStatisticsForTheAllTime()
    {
        await using var context = new DataContext(_dbPath);
        return await context.KeyStatisticsForTheDay.SumAsync(k=> k.ClickCount);
    }
}