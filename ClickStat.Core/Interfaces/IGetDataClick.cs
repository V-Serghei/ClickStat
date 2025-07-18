using ClickStat.Infrastructure.Data.Model;

namespace ClickStat.Core.Interfaces;

public interface IGetDataClick
{
    Task<List<KeyStatistics>> GetKeyStatistics();
    Task<List<KeyStatistics>> GetKeyStatisticsByKeyCode(int keyCode);
    Task<List<KeyStatistics>> GetKeyStatisticsByKeyName(string keyName);
    Task<List<KeyStatisticsForTheDay>> GetKeyStatisticsForTheDay(DateTime date);//тут возращается только одна запись в списке по дате
    Task<int> GetKeyStatisticsForTheAllTime();
}