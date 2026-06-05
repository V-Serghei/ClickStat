using System.Collections.Generic;
using System.Threading.Tasks;
using ClickStat.Infrastructure.Data.Model;

namespace ClickStat.Core.Interfaces;

public interface IMouseStatisticsService
{
    bool IsRegistered(int buttonCode);
    void TrackButtonClick(int buttonCode, string buttonName);
    void TrackScroll(int notches);
    void TrackClickPosition();
    void TrackMovement(int dx, int dy);
    Task RegisterCustomButton(int buttonCode, string buttonName);
    Task<List<MouseStatistics>> GetButtonStatistics();
    Task<MouseScrollStatistics?> GetScrollStatistics();
    Task FlushAsync();
    Task OnApplicationExitAsync();
}
