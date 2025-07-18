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
        _context.Database.EnsureCreated();
    }

    public async Task<List<KeyStatistics>> GetKeyStatistics()
    {
        return await _context.KeyStatistics.ToListAsync();
    }

    public async Task<List<KeyStatistics>> GetKeyStatisticsByKeyCode(int keyCode)
    {
        var keyStatistics = await _context.KeyStatistics
            .Where(k => k.KeyCode == keyCode)
            .ToListAsync();
        return keyStatistics;
    }

    public async Task<List<KeyStatistics>> GetKeyStatisticsByKeyName(string keyName)
    {
        var keyStatistics = await _context.KeyStatistics
            .Where(k => k.KeyName.Equals(keyName, StringComparison.OrdinalIgnoreCase))
            .ToListAsync();
        return keyStatistics;
    }

    public async Task<List<KeyStatisticsForTheDay>> GetKeyStatisticsForTheDay(DateTime date)
    {
        
            var keyStatisticsForTheDay = await _context.KeyStatisticsForTheDay
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
                _context.KeyStatisticsForTheDay.RemoveRange(duplicates);

                await _context.SaveChangesAsync();
            }
            else if (keyStatisticsForTheDay.Count == 0)
            {
                var newRecord = new KeyStatisticsForTheDay
                {
                    DayId = Guid.NewGuid(),
                    Date = date.Date,
                    ClickCount = 0
                };
                _context.KeyStatisticsForTheDay.Add(newRecord);
                await _context.SaveChangesAsync();
                keyStatisticsForTheDay.Add(newRecord);
            }

            
            return keyStatisticsForTheDay;
        
        
    }


public async Task<int> GetKeyStatisticsForTheAllTime()
    {
        return await _context.KeyStatisticsForTheDay.CountAsync();
    }
}