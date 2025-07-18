using ClickStat.Core.Interfaces;
using ClickStat.Infrastructure.Data;
using ClickStat.Infrastructure.Data.Model;

namespace ClickStat.Core.Services;

public class GetDataClickService : IGetDataClick
{
    private readonly KeyGetDataClick _keyGetDataClick = new();
    
    
    public async Task<List<KeyStatistics>> GetKeyStatistics()
    {
        return await _keyGetDataClick.GetKeyStatistics();
    }

    public async Task<List<KeyStatistics>> GetKeyStatisticsByKeyCode(int keyCode)
    {
        return await _keyGetDataClick.GetKeyStatisticsByKeyCode(keyCode);
    }

    public async Task< List<KeyStatistics>> GetKeyStatisticsByKeyName(string keyName)
    {
        return await _keyGetDataClick.GetKeyStatisticsByKeyName(keyName);
    }

    public async Task<List<KeyStatisticsForTheDay>> GetKeyStatisticsForTheDay(DateTime date)
    {
        return await _keyGetDataClick.GetKeyStatisticsForTheDay(date);
    }

    public async Task<int> GetKeyStatisticsForTheAllTime()
    {
        return await _keyGetDataClick.GetKeyStatisticsForTheAllTime();
    }
}