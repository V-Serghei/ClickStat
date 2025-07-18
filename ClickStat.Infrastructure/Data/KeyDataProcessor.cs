using System.IO;
using System.Windows.Forms;
using ClickStat.Infrastructure.Data.Context;
using ClickStat.Infrastructure.Data.Model;
using Timer = System.Timers.Timer;

namespace ClickStat.Infrastructure.Data;

public class KeyDataProcessor
{
    private const int SaveIntervalSeconds = 5;
    private const int MaxDelaySeconds = 20;
    private readonly DataContext _context;
    private readonly string _dbPath;
    private readonly Dictionary<Keys, KeyStatistics> _keyStatistics = new();
    private readonly Timer _saveTimer;
    private DateTime _lastSaveTime = DateTime.MinValue;

    public KeyDataProcessor()
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

        _saveTimer = new Timer(SaveIntervalSeconds * 1000);
        _saveTimer.Elapsed += async (s, e) => await SaveToDatabaseBuffered();
        _saveTimer.AutoReset = true;
        _saveTimer.Start();
    }

    private void InitializeDatabase()
    {
        _context.Database.EnsureCreated();
    }

    private void LoadDataFromDatabase()
    {
        var stats = _context.KeyStatistics.ToList();
        foreach (var stat in stats)
            _keyStatistics[(Keys)stat.KeyCode] = new KeyStatistics
            {
                KeyCode = stat.KeyCode,
                KeyName = stat.KeyName,
                Count = stat.Count
            };
    }


    public async Task ProcessKeyPress(Keys key)
    {
        lock (_keyStatistics)
        {
            if (_keyStatistics.ContainsKey(key))
                _keyStatistics[key].Count++;
            else
                _keyStatistics[key] = new KeyStatistics { KeyCode = (int)key, KeyName = key.ToString(), Count = 1 };
        }

        var timeSinceLastSave = (DateTime.Now - _lastSaveTime).TotalSeconds;
        if (timeSinceLastSave >= MaxDelaySeconds)
            _ = SaveToDatabaseBuffered();
    }

    private async Task SaveToDatabaseBuffered()
    {
        using var context = new DataContext(_dbPath);
        var transaction = await context.Database.BeginTransactionAsync();

        try
        {
            foreach (var stat in _keyStatistics.ToList())
            {
                var existingStat = await context.KeyStatistics.FindAsync(stat.Value.KeyCode);
                if (existingStat != null)
                    existingStat.Count = stat.Value.Count;
                else
                    context.KeyStatistics.Add(new KeyStatistics
                    {
                        KeyCode = stat.Value.KeyCode,
                        KeyName = stat.Value.KeyName,
                        Count = stat.Value.Count
                    });
            }

            await context.SaveChangesAsync();
            await transaction.CommitAsync();
            _lastSaveTime = DateTime.Now;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            Console.WriteLine($"Save failed: {ex.Message}");
        }
    }

    public async Task OnApplicationExitAsync()
    {
        _saveTimer.Stop();
        await SaveToDatabaseBuffered();
    }
}