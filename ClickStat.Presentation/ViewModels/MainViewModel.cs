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
        private readonly IMouseMonitorService _mouseMonitorService;
        private readonly ISavingClick _savingClickService;
        private readonly IMouseStatisticsService _mouseStatisticsService;
        private readonly IGetDataClick _getDataClickService;
        private readonly IStartupService _startupService;

        public StatisticsViewModel StatisticsVm { get; }
        public KeyboardViewModel KeyboardVm { get; }
        public MouseViewModel MouseVm { get; }

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
                    if (_isInStartup) _startupService.AddToStartup();
                    else _startupService.RemoveFromStartup();
                }
            }
        }

        public MainViewModel(
            IInputMonitorService inputMonitorService,
            IMouseMonitorService mouseMonitorService,
            ISavingClick savingClickService,
            IMouseStatisticsService mouseStatisticsService,
            StatisticsViewModel statisticsVm,
            KeyboardViewModel keyboardVm,
            MouseViewModel mouseVm,
            IGetDataClick dataService,
            IStartupService startupService)
        {
            _inputMonitorService = inputMonitorService;
            _mouseMonitorService = mouseMonitorService;
            _savingClickService = savingClickService;
            _mouseStatisticsService = mouseStatisticsService;
            _getDataClickService = dataService;
            _startupService = startupService;

            StatisticsVm = statisticsVm;
            KeyboardVm = keyboardVm;
            MouseVm = mouseVm;

            _inputMonitorService.OnKeyAction += OnKeyReceived;
            _inputMonitorService.StartMonitoring();

            _mouseMonitorService.OnButtonPressed += OnMouseButtonPressed;
            _mouseMonitorService.OnScroll += OnMouseScrolled;
            _mouseMonitorService.StartMonitoring();

            RefreshDataCommand = new RelayCommand(ExecuteRefreshData);
            _isInStartup = _startupService.IsInStartup();
        }

        private async void ExecuteRefreshData(object parameter)
        {
            await StatisticsVm.LoadStatsAsync();
            await KeyboardVm.LoadKeyCountsAsync();
            await MouseVm.LoadDataAsync();
        }

        private void OnKeyReceived(Keys key) => _savingClickService.SaveClick(key);

        private void OnMouseButtonPressed(MouseButtons button, int buttonCode)
        {
            if (_mouseStatisticsService.IsRegistered(buttonCode))
            {
                var name = button switch
                {
                    MouseButtons.Left     => "Левая кнопка",
                    MouseButtons.Right    => "Правая кнопка",
                    MouseButtons.Middle   => "Колесо (клик)",
                    MouseButtons.XButton1 => "Кнопка назад",
                    MouseButtons.XButton2 => "Кнопка вперёд",
                    _                     => $"Кнопка {buttonCode}"
                };
                _mouseStatisticsService.TrackButtonClick(buttonCode, name);
            }
        }

        private void OnMouseScrolled(int notches) => _mouseStatisticsService.TrackScroll(notches);

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private string _title = "ClickStat Application";
        public string Title
        {
            get => _title;
            set
            {
                if (_title != value) { _title = value; OnPropertyChanged(); }
            }
        }
    }

    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Func<object, bool>? _canExecute;

        public RelayCommand(Action<object> execute, Func<object, bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter!) ?? true;
        public void Execute(object? parameter) => _execute(parameter!);
        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }
}
