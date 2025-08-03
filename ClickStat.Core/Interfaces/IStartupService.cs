namespace ClickStat.Core.Interfaces;

public interface IStartupService
{
    bool IsInStartup();
    void AddToStartup();
    void RemoveFromStartup();
}