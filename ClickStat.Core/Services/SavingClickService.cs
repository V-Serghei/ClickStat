using System.Windows.Forms;
using ClickStat.Core.Interfaces;
using ClickStat.Infrastructure.Data;

namespace ClickStat.Core.Services;

public class SavingClickService: ISavingClick
{
    private readonly KeyDataProcessor _keyData = new();

    public async Task SaveClick(Keys key)
    {
        Console.WriteLine($"Save Click: {key}");
        await _keyData.ProcessKeyPress(key);
        
    }
    public async Task OnApplicationExitAsync()
    {
        Console.WriteLine("The application is closing, saving data...");
        await _keyData.OnApplicationExitAsync();
    }
}