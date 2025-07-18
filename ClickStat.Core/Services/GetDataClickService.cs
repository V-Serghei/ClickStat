using ClickStat.Core.Interfaces;
using ClickStat.Infrastructure.Data;
using ClickStat.Infrastructure.Data.Model;

namespace ClickStat.Core.Services;

public class GetDataClickService : IGetDataClick
{
    private readonly KeyGetDataClick _keyGetDataClick = new();
    private readonly List<KeyStatistics> _keyStats;

        public GetDataClickService()
        {
            _keyStats = new List<KeyStatistics>
            {
                new KeyStatistics { KeyCode = 8, KeyName = "Back", Count = 381 },
                new KeyStatistics { KeyCode = 9, KeyName = "Tab", Count = 52 },
                new KeyStatistics { KeyCode = 13, KeyName = "Enter", Count = 39 },
                new KeyStatistics { KeyCode = 20, KeyName = "Capital", Count = 92 },
                new KeyStatistics { KeyCode = 27, KeyName = "Escape", Count = 146 },
                new KeyStatistics { KeyCode = 32, KeyName = "Space", Count = 717 },
                new KeyStatistics { KeyCode = 37, KeyName = "Left", Count = 29 },
                new KeyStatistics { KeyCode = 38, KeyName = "Up", Count = 30 },
                new KeyStatistics { KeyCode = 39, KeyName = "Right", Count = 26 },
                new KeyStatistics { KeyCode = 40, KeyName = "Down", Count = 33 },
                new KeyStatistics { KeyCode = 46, KeyName = "Delete", Count = 3 },
                new KeyStatistics { KeyCode = 48, KeyName = "D0", Count = 12 },
                new KeyStatistics { KeyCode = 49, KeyName = "D1", Count = 72 },
                new KeyStatistics { KeyCode = 50, KeyName = "D2", Count = 66 },
                new KeyStatistics { KeyCode = 51, KeyName = "D3", Count = 68 },
                new KeyStatistics { KeyCode = 52, KeyName = "D4", Count = 4 },
                new KeyStatistics { KeyCode = 53, KeyName = "D5", Count = 1 },
                new KeyStatistics { KeyCode = 54, KeyName = "D6", Count = 1 },
                new KeyStatistics { KeyCode = 55, KeyName = "D7", Count = 2 },
                new KeyStatistics { KeyCode = 56, KeyName = "D8", Count = 1 },
                new KeyStatistics { KeyCode = 65, KeyName = "A", Count = 41 },
                new KeyStatistics { KeyCode = 66, KeyName = "B", Count = 184 },
                new KeyStatistics { KeyCode = 67, KeyName = "C", Count = 164 },
                new KeyStatistics { KeyCode = 68, KeyName = "D", Count = 149 },
                new KeyStatistics { KeyCode = 69, KeyName = "E", Count = 91 },
                new KeyStatistics { KeyCode = 70, KeyName = "F", Count = 260 },
                new KeyStatistics { KeyCode = 71, KeyName = "G", Count = 103 },
                new KeyStatistics { KeyCode = 72, KeyName = "H", Count = 99 },
                new KeyStatistics { KeyCode = 73, KeyName = "I", Count = 31 },
                new KeyStatistics { KeyCode = 74, KeyName = "J", Count = 329 },
                new KeyStatistics { KeyCode = 75, KeyName = "K", Count = 103 },
                new KeyStatistics { KeyCode = 76, KeyName = "L", Count = 88 },
                new KeyStatistics { KeyCode = 77, KeyName = "M", Count = 72 },
                new KeyStatistics { KeyCode = 78, KeyName = "N", Count = 264 },
                new KeyStatistics { KeyCode = 79, KeyName = "O", Count = 15 },
                new KeyStatistics { KeyCode = 80, KeyName = "P", Count = 58 },
                new KeyStatistics { KeyCode = 81, KeyName = "Q", Count = 31 },
                new KeyStatistics { KeyCode = 82, KeyName = "R", Count = 133 },
                new KeyStatistics { KeyCode = 83, KeyName = "S", Count = 96 },
                new KeyStatistics { KeyCode = 84, KeyName = "T", Count = 234 },
                new KeyStatistics { KeyCode = 85, KeyName = "U", Count = 79 },
                new KeyStatistics { KeyCode = 86, KeyName = "V", Count = 111 },
                new KeyStatistics { KeyCode = 87, KeyName = "W", Count = 17 },
                new KeyStatistics { KeyCode = 88, KeyName = "X", Count = 74 },
                new KeyStatistics { KeyCode = 89, KeyName = "Y", Count = 154 },
                new KeyStatistics { KeyCode = 90, KeyName = "Z", Count = 72 },
                new KeyStatistics { KeyCode = 91, KeyName = "LWin", Count = 26 },
                new KeyStatistics { KeyCode = 160, KeyName = "LShiftKey", Count = 103 },
                new KeyStatistics { KeyCode = 162, KeyName = "LControlKey", Count = 55 },
                new KeyStatistics { KeyCode = 164, KeyName = "LMenu", Count = 37 },
                 new KeyStatistics { KeyCode = 188, KeyName = "Oemcomma", Count = 55 },
                new KeyStatistics { KeyCode = 190, KeyName = "OemPeriod", Count = 33 },
            };
        }
    
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