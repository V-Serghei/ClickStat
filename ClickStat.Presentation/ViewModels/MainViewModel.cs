using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using ClickStat.Core.Interfaces;
using System.Windows.Forms;
using System.Windows.Input;

namespace ClickStat.Presentation.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly IInputMonitorService _inputMonitorService;
    private readonly ISavingClick _savingClickService;
    private readonly IGetDataClick _getDataClickService;
    
    
    public MainViewModel(IInputMonitorService inputMonitorService, ISavingClick savingClickService,IGetDataClick dataService)
    {
        _inputMonitorService = inputMonitorService;
        _inputMonitorService.OnKeyAction += OnKeyReceived;
        _inputMonitorService.StartMonitoring();
        _savingClickService = savingClickService;
        _getDataClickService = dataService;
       
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