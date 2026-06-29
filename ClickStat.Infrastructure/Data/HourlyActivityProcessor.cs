using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ClickStat.Infrastructure.Data.Context;
using ClickStat.Infrastructure.Data.Model;
using Microsoft.EntityFrameworkCore;
using Timer = System.Timers.Timer;

namespace ClickStat.Infrastructure.Data;

public sealed class HourlyActivityProcessor : IDisposable
{
    private const int FlushIntervalSeconds = 30;

    private readonly string _dbPath;
    private readonly Timer  _timer;
    private readonly object _lock = new();
    private readonly object _schemaGate = new();
    private readonly Dictionary<int, int> _buffer = new(); // Id → count delta
    private bool _schemaReady;

    public HourlyActivityProcessor()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        _dbPath  = Path.Combine(docs, "KeyClick", "key_statistics.db");

        _timer = new Timer(FlushIntervalSeconds * 1000) { AutoReset = true };
        _timer.Elapsed += async (_, _) => await Flush();
        _timer.Start();
    }

    public void Record()
    {
        var now = DateTime.Now;
        int id  = (int)now.DayOfWeek * 24 + now.Hour;
        lock (_lock)
            _buffer[id] = _buffer.GetValueOrDefault(id) + 1;
    }

    public async Task<List<HourlyActivity>> GetAll()
    {
        await using var ctx = new DataContext(_dbPath);
        EnsureAllSlots(ctx);
        return await ctx.HourlyActivities.OrderBy(h => h.Id).ToListAsync();
    }

    private void EnsureAllSlots(DataContext ctx)
    {
        lock (_schemaGate)
        {
            if (_schemaReady)
                return;

            ctx.Database.EnsureCreated();
            ctx.Database.ExecuteSqlRaw(@"
                CREATE TABLE IF NOT EXISTS HourlyActivities (
                    Id        INTEGER PRIMARY KEY,
                    DayOfWeek INTEGER NOT NULL,
                    Hour      INTEGER NOT NULL,
                    Count     INTEGER NOT NULL DEFAULT 0
                )");

            for (int d = 0; d < 7; d++)
            for (int h = 0; h < 24; h++)
            {
                int id = d * 24 + h;
                ctx.Database.ExecuteSqlRaw(
                    "INSERT OR IGNORE INTO HourlyActivities (Id, DayOfWeek, Hour, Count) VALUES ({0}, {1}, {2}, 0)",
                    id, d, h);
            }

            _schemaReady = true;
        }
    }

    private async Task Flush()
    {
        Dictionary<int, int> snap;
        lock (_lock)
        {
            if (_buffer.Count == 0) return;
            snap = new Dictionary<int, int>(_buffer);
            _buffer.Clear();
        }

        await using var ctx = new DataContext(_dbPath);
        EnsureAllSlots(ctx);
        var tx = await ctx.Database.BeginTransactionAsync();
        try
        {
            foreach (var (id, delta) in snap)
            {
                var row = await ctx.HourlyActivities.FindAsync(id);
                if (row != null) row.Count += delta;
            }
            await ctx.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch { await tx.RollbackAsync(); }
    }

    public async Task OnApplicationExitAsync() { _timer.Stop(); await Flush(); }
    public void Dispose() { _timer.Stop(); _timer.Dispose(); }
}
