using ClickStat.Core.Interfaces;
using ClickStat.Infrastructure.Data;
using ClickStat.Infrastructure.Data.Model;

namespace ClickStat.Core.Services;

public class GetDataClickService : IGetDataClick
{
    private readonly KeyGetDataClick _keyGetDataClick = new();

    public Task<List<KeyStatistics>> GetKeyStatistics() =>
        _keyGetDataClick.GetKeyStatistics();

    public Task<List<KeyStatistics>> GetKeyStatisticsByKeyCode(int keyCode) =>
        _keyGetDataClick.GetKeyStatisticsByKeyCode(keyCode);

    public Task<List<KeyStatistics>> GetKeyStatisticsByKeyName(string keyName) =>
        _keyGetDataClick.GetKeyStatisticsByKeyName(keyName);

    public Task<List<KeyStatisticsForTheDay>> GetKeyStatisticsForTheDay(DateTime date) =>
        _keyGetDataClick.GetKeyStatisticsForTheDay(date);

    public Task<int> GetKeyStatisticsForTheAllTime() =>
        _keyGetDataClick.GetKeyStatisticsForTheAllTime();

    public Task<Dictionary<DateTime, int>> GetDailyClickCounts(DateTime from, DateTime to) =>
        _keyGetDataClick.GetDailyClickCounts(from, to);
}
