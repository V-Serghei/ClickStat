using System.Windows.Forms;
using ClickStat.Core.Interfaces;
using ClickStat.Infrastructure.Data;

namespace ClickStat.Core.Services;

public class SavingClickService: ISavingClick
{
    private readonly KeyDataProcessor _keyData = new();

    public Task SaveClick(Keys key) => _keyData.ProcessKeyPress(key);

    public Task FlushAsync() => _keyData.FlushAsync();

    public Task OnApplicationExitAsync() => _keyData.OnApplicationExitAsync();
}