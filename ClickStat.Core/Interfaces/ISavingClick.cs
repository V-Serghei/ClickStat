using System.Windows.Forms;

namespace ClickStat.Core.Interfaces;

public interface ISavingClick
{
    Task SaveClick(Keys key);
    Task FlushAsync();
    Task OnApplicationExitAsync();
}