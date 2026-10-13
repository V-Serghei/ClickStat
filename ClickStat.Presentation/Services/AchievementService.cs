using System.Globalization;
using System.IO;
using ClickStat.Core.Interfaces;
using Microsoft.Data.Sqlite;

namespace ClickStat.Presentation.Services;

public sealed record AchievementProgress(string Id, string Icon, string Category, string TitleKey,
    string DescriptionKey, long Current, long Target, DateTimeOffset? UnlockedAt)
{
    public double Percent => Math.Min(100, Current * 100.0 / Target);
    public bool IsUnlocked => UnlockedAt != null;
}

public sealed class AchievementService
{
    private readonly string _connectionString;
    private readonly ITrayService _tray;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly System.Windows.Threading.DispatcherTimer _timer;

    public AchievementService(ITrayService tray)
    {
        _tray = tray;
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "KeyClick", "key_statistics.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared, DefaultTimeout = 5
        }.ToString();
        _timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(5)
        };
        _timer.Tick += async (_, _) =>
        {
            try { await RefreshAsync(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Achievement refresh: {ex}"); }
        };
        _timer.Start();
    }

    public Task<IReadOnlyList<AchievementProgress>> RefreshAsync() => Task.Run(RefreshCoreAsync);

    private async Task<IReadOnlyList<AchievementProgress>> RefreshCoreAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await using var db = new SqliteConnection(_connectionString);
            await db.OpenAsync();
            await using (var schema = db.CreateCommand())
            {
                schema.CommandText = """
                    CREATE TABLE IF NOT EXISTS AchievementUnlocks (
                        Id TEXT PRIMARY KEY,
                        UnlockedAt TEXT NOT NULL
                    );
                    """;
                await schema.ExecuteNonQueryAsync();
            }

            var unlocked = new Dictionary<string, DateTimeOffset>();
            await using (var query = db.CreateCommand())
            {
                query.CommandText = "SELECT Id, UnlockedAt FROM AchievementUnlocks";
                await using var reader = await query.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    if (DateTimeOffset.TryParse(reader.GetString(1), CultureInfo.InvariantCulture,
                            DateTimeStyles.RoundtripKind, out var date))
                        unlocked[reader.GetString(0)] = date;
            }

            var daily = new SortedDictionary<DateTime, long>();
            if (await TableExistsAsync(db, "KeyStatisticsForTheDay"))
            {
                await using var query = db.CreateCommand();
                query.CommandText = "SELECT Date, SUM(ClickCount) FROM KeyStatisticsForTheDay GROUP BY Date";
                await using var reader = await query.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    if (DateTime.TryParse(reader.GetString(0), out var date))
                        daily[date.Date] = reader.GetInt64(1);
            }

            long keys = await SumAsync(db, "KeyStatisticsForTheDay", "ClickCount");
            long days = daily.Count(pair => pair.Value > 0);
            long bestDay = daily.Values.DefaultIfEmpty().Max();
            long bestStreak = BestStreak(daily);
            long mouse = await SumAsync(db, "MouseStatistics", "Count");
            long scroll = await SumAsync(db, "MouseScrollStatistics", "ScrollUpNotches + ScrollDownNotches");
            long distanceMeters = await SumAsync(db, "MouseDistances", "TotalUnits") / 100_000;
            long apps = await CountAsync(db, "AppUsageStatistics", "KeyCount + ClickCount > 0");
            long appKeys = await SumAsync(db, "AppUsageStatistics", "KeyCount");
            long training = await CountAsync(db, "TypingTrainingResults", "1=1");
            long cleanTraining = await CountAsync(db, "TypingTrainingResults", "Accuracy >= 99.9");
            long trainingChars = await SumAsync(db, "TypingTrainingResults", "CorrectChars");
            long bestWpm = await MaxAsync(db, "TypingTrainingResults", "Wpm");
            long languages = await DistinctCountAsync(db, "TypingTrainingResults", "Language");
            long lateKeys = await FilteredSumAsync(db, "HourlyActivities", "Count", "Hour >= 22 OR Hour < 5");
            long weekendKeys = await FilteredSumAsync(db, "HourlyActivities", "Count", "DayOfWeek IN (0, 6)");

            var definitions = new List<(string id, string icon, string category, long value, long target)>
            {
                ("first-steps", "⌨", "Keyboard", keys, 100),
                ("warmup", "⌨", "Keyboard", keys, 1_000),
                ("ten-thousand", "⌨", "Keyboard", keys, 10_000),
                ("key-river", "⌨", "Keyboard", keys, 100_000),
                ("key-mountain", "⌨", "Keyboard", keys, 1_000_000),
                ("one-day", "◷", "Rhythm", days, 1),
                ("seven-days", "◷", "Rhythm", days, 7),
                ("thirty-days", "◷", "Rhythm", days, 30),
                ("year-of-days", "◷", "Rhythm", days, 365),
                ("three-day-streak", "✦", "Rhythm", bestStreak, 3),
                ("week-streak", "✦", "Rhythm", bestStreak, 7),
                ("month-streak", "✦", "Rhythm", bestStreak, 30),
                ("busy-day", "☀", "Rhythm", bestDay, 1_000),
                ("marathon-day", "☀", "Rhythm", bestDay, 10_000),
                ("night-notes", "☾", "Rhythm", lateKeys, 500),
                ("weekend-maker", "☀", "Rhythm", weekendKeys, 5_000),
                ("first-clicks", "🖱", "Mouse", mouse, 100),
                ("click-craft", "🖱", "Mouse", mouse, 10_000),
                ("click-constellation", "🖱", "Mouse", mouse, 100_000),
                ("scroll-journey", "↕", "Mouse", scroll, 1_000),
                ("scroll-atlas", "↕", "Mouse", scroll, 10_000),
                ("cursor-mile", "↗", "Mouse", distanceMeters, 1_609),
                ("first-session", "⚡", "Training", training, 1),
                ("ten-sessions", "⚡", "Training", training, 10),
                ("hundred-sessions", "⚡", "Training", training, 100),
                ("perfect-line", "✓", "Training", cleanTraining, 1),
                ("perfect-ten", "✓", "Training", cleanTraining, 10),
                ("speed-40", "⚡", "Training", bestWpm, 40),
                ("speed-80", "⚡", "Training", bestWpm, 80),
                ("training-thousand", "✎", "Training", trainingChars, 1_000),
                ("training-ten-thousand", "✎", "Training", trainingChars, 10_000),
                ("two-languages", "🌐", "Training", languages, 2),
                ("three-apps", "▣", "Apps", apps, 3),
                ("ten-apps", "▣", "Apps", apps, 10),
                ("app-explorer", "▣", "Apps", apps, 30),
                ("app-keys", "▣", "Apps", appKeys, 10_000)
            };

            var newlyUnlocked = new List<string>();
            var result = new List<AchievementProgress>(definitions.Count);
            foreach (var item in definitions)
            {
                if (!unlocked.TryGetValue(item.id, out var date) && item.value >= item.target)
                {
                    date = DateTimeOffset.Now;
                    await using var insert = db.CreateCommand();
                    insert.CommandText = "INSERT OR IGNORE INTO AchievementUnlocks (Id, UnlockedAt) VALUES ($id, $at)";
                    insert.Parameters.AddWithValue("$id", item.id);
                    insert.Parameters.AddWithValue("$at", date.ToString("O"));
                    if (await insert.ExecuteNonQueryAsync() > 0)
                        newlyUnlocked.Add(item.id);
                    unlocked[item.id] = date;
                }
                result.Add(new AchievementProgress(item.id, item.icon, item.category,
                    "Achievement." + item.id + ".Title", "Achievement." + item.id + ".Description",
                    item.value, item.target, unlocked.GetValueOrDefault(item.id) is { } saved ? saved : null));
            }

            if (newlyUnlocked.Count > 0)
            {
                var loc = LocalizationService.Instance;
                var title = loc["Achievements.Unlocked"];
                var first = loc["Achievement." + newlyUnlocked[0] + ".Title"];
                var message = newlyUnlocked.Count == 1 ? first : $"{first} (+{newlyUnlocked.Count - 1})";
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    _tray.ShowNotification(title, message));
            }

            return result;
        }
        finally { _gate.Release(); }
    }

    private static async Task<bool> TableExistsAsync(SqliteConnection db, string table)
    {
        await using var command = db.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$name";
        command.Parameters.AddWithValue("$name", table);
        return await command.ExecuteScalarAsync() != null;
    }

    private static async Task<long> ScalarAsync(SqliteConnection db, string table, string expression, string aggregate, string? where = null)
    {
        if (!await TableExistsAsync(db, table)) return 0;
        await using var command = db.CreateCommand();
        command.CommandText = $"SELECT IFNULL({aggregate}({expression}), 0) FROM {table}" +
            (where == null ? "" : $" WHERE {where}");
        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static Task<long> SumAsync(SqliteConnection db, string table, string expression) => ScalarAsync(db, table, expression, "SUM");
    private static Task<long> FilteredSumAsync(SqliteConnection db, string table, string expression, string where) => ScalarAsync(db, table, expression, "SUM", where);
    private static Task<long> MaxAsync(SqliteConnection db, string table, string expression) => ScalarAsync(db, table, expression, "MAX");
    private static Task<long> CountAsync(SqliteConnection db, string table, string where) => ScalarAsync(db, table, "*", "COUNT", where);
    private static Task<long> DistinctCountAsync(SqliteConnection db, string table, string expression) => ScalarAsync(db, table, "DISTINCT " + expression, "COUNT");

    private static long BestStreak(SortedDictionary<DateTime, long> daily)
    {
        long best = 0, current = 0;
        DateTime? previous = null;
        foreach (var item in daily.Where(pair => pair.Value > 0))
        {
            current = previous == item.Key.AddDays(-1) ? current + 1 : 1;
            best = Math.Max(best, current);
            previous = item.Key;
        }
        return best;
    }
}
