using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using ClickStat.Core.Interfaces;
using System.Windows.Forms;
namespace ClickStat.Presentation.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly IInputMonitorService _inputMonitorService;
    private readonly ISavingClick _savingClickService;
        
    public MainViewModel(IInputMonitorService inputMonitorService, ISavingClick savingClickService)
    {
        _inputMonitorService = inputMonitorService;
        _inputMonitorService.OnKeyAction += OnKeyReceived;
        _inputMonitorService.StartMonitoring();
        _savingClickService = savingClickService;
    }

    private void OnKeyReceived(Keys key) { _savingClickService.SaveClick(key); }
        
    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
        
    private string _title = "ClickStat Application";
    public string Title
    {
        get => _title;
        set
        {
            if (_title != value)
            {
                _title = value;
                OnPropertyChanged();
            }
        }
    }
}