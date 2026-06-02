using ClickStat.Infrastructure.Data.Model;

namespace ClickStat.Core.Interfaces;

public interface IGetDataClick
{
    Task<List<KeyStatistics>> GetKeyStatistics();
    Task<List<KeyStatistics>> GetKeyStatisticsByKeyCode(int keyCode);
    Task<List<KeyStatistics>> GetKeyStatisticsByKeyName(string keyName);
    Task<List<KeyStatisticsForTheDay>> GetKeyStatisticsForTheDay(DateTime date);
    Task<int> GetKeyStatisticsForTheAllTime();
    /// <summary>One DB query for all days in [from, to]. Returns 0 for days with no data.</summary>
    Task<Dictionary<DateTime, int>> GetDailyClickCounts(DateTime from, DateTime to);
}