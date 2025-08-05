using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ClickStat.Core.Interfaces;
using System.Windows.Forms;

namespace ClickStat.Presentation.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly IInputMonitorService _inputMonitorService;
        private readonly ISavingClick _savingClickService;
        private readonly IGetDataClick _getDataClickService;
        private readonly IStartupService _startupService;
        public StatisticsViewModel StatisticsVm { get; }
        public KeyboardViewModel KeyboardVm { get; }
        public ICommand RefreshDataCommand { get; }
        private bool _isInStartup;
        public bool IsInStartup
        {
            get => _isInStartup;
            set
            {
                if (_isInStartup != value)
                {
                    _isInStartup = value;
                    OnPropertyChanged();
                    if (_isInStartup)
                    {
                        _startupService.AddToStartup();
                    }
                    else
                    {
                        _startupService.RemoveFromStartup();
                    }
                }
            }
        }

        public MainViewModel(IInputMonitorService inputMonitorService,
            ISavingClick savingClickService,
            StatisticsViewModel statisticsVm,
            KeyboardViewModel keyboardVm,
            IGetDataClick dataService,
            IStartupService startupService)
        {
            _inputMonitorService = inputMonitorService;
            _inputMonitorService.OnKeyAction += OnKeyReceived;
            _inputMonitorService.StartMonitoring();
            _savingClickService = savingClickService;
            _getDataClickService = dataService;
            _startupService = startupService;
            StatisticsVm = statisticsVm;
            KeyboardVm = keyboardVm;
            RefreshDataCommand = new RelayCommand(ExecuteRefreshData);
            _isInStartup = _startupService.IsInStartup();
        }

        private async void ExecuteRefreshData(object parameter)
        {
            await StatisticsVm.LoadStatsAsync();
            await KeyboardVm.LoadKeyCountsAsync();
        }

        private void OnKeyReceived(Keys key)
        {
            _savingClickService.SaveClick(key);
         }

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

    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Func<object, bool> _canExecute;

        public RelayCommand(Action<object> execute, Func<object, bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter) => _canExecute?.Invoke(parameter) ?? true;
        public void Execute(object parameter) => _execute(parameter);
        public event EventHandler CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }
}