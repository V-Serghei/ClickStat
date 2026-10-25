using System.Collections.Generic;
using System.Threading.Tasks;
using ClickStat.Core.Interfaces;
using ClickStat.Infrastructure.Data;
using ClickStat.Infrastructure.Data.Model;

namespace ClickStat.Core.Services;

public class MouseStatisticsService : IMouseStatisticsService
{
    private readonly MouseDataProcessor _processor;

    public MouseStatisticsService(MouseDataProcessor processor) => _processor = processor;

    public bool IsRegistered(int buttonCode) => _processor.IsRegistered(buttonCode);

    public void TrackButtonClick(int buttonCode, string buttonName) =>
        _processor.TrackButtonClick(buttonCode, buttonName);

    public void TrackScroll(int notches)        => _processor.TrackScroll(notches);
    public void TrackClickPosition()            => _processor.TrackClickPosition();
    public void TrackMovement(long distanceUnits, int x, int y) => _processor.TrackMovement(distanceUnits, x, y);

    public Task RegisterCustomButton(int buttonCode, string buttonName) =>
        _processor.RegisterCustomButton(buttonCode, buttonName);

    public Task<List<MouseStatistics>> GetButtonStatistics() =>
        _processor.GetButtonStatistics();

    public Task<MouseScrollStatistics?> GetScrollStatistics() =>
        _processor.GetScrollStatistics();
    public Task<CursorHeatmap> GetCursorHeatmapAsync() =>
        _processor.GetCursorHeatmapAsync();

    public Task FlushAsync()             => _processor.FlushAsync();
    public Task OnApplicationExitAsync() => _processor.OnApplicationExitAsync();
}
