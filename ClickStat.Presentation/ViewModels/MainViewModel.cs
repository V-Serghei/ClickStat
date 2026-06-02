using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ClickStat.Core.Helpers;
using ClickStat.Core.Interfaces;
using ClickStat.Presentation.Services;
using System.Windows.Forms;

namespace ClickStat.Presentation.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly IInputMonitorService      _inputMonitorService;
        private readonly IMouseMonitorService      _mouseMonitorService;
        private readonly ISavingClick              _savingClickService;
        private readonly IMouseStatisticsService   _mouseStatisticsService;
        private readonly IStartupService           _startupService;
        private readonly LiveEventBus              _liveBus;

        public StatisticsViewModel StatisticsVm { get; }
        public KeyboardViewModel   KeyboardVm   { get; }
        public MouseViewModel      MouseVm      { get; }

        public ICommand RefreshDataCommand { get; }

        private bool _isInStartup;
        public bool IsInStartup
        {
            get => _isInStartup;
            set
            {
                if (_isInStartup == value) return;
                _isInStartup = value;
                OnPropertyChanged();
                if (_isInStartup) _startupService.AddToStartup();
                else _startupService.RemoveFromStartup();
            }
        }

        public MainViewModel(
            IInputMonitorService    inputMonitorService,
            IMouseMonitorService    mouseMonitorService,
            ISavingClick            savingClickService,
            IMouseStatisticsService mouseStatisticsService,
            StatisticsViewModel     statisticsVm,
            KeyboardViewModel       keyboardVm,
            MouseViewModel          mouseVm,
            IGetDataClick           dataService,
            IStartupService         startupService,
            LiveEventBus            liveBus)
        {
            _inputMonitorService    = inputMonitorService;
            _mouseMonitorService    = mouseMonitorService;
            _savingClickService     = savingClickService;
            _mouseStatisticsService = mouseStatisticsService;
            _startupService         = startupService;
            _liveBus                = liveBus;

            StatisticsVm = statisticsVm;
            KeyboardVm   = keyboardVm;
            MouseVm      = mouseVm;

            _inputMonitorService.OnKeyDown   += OnKeyDownReceived;
            _inputMonitorService.OnKeyAction += OnKeyReceived;
            _inputMonitorService.StartMonitoring();

            _mouseMonitorService.OnButtonPressed += OnMouseButtonPressed;
            _mouseMonitorService.OnScroll        += OnMouseScrolled;
            _mouseMonitorService.StartMonitoring();

            RefreshDataCommand = new RelayCommand(ExecuteRefreshData);
            _isInStartup = _startupService.IsInStartup();
        }

        // ── Tab visibility management ──────────────────────────────────────

        public void SetActiveTab(int tabIndex)
        {
            KeyboardVm.IsLiveActive = (tabIndex == 1);
            MouseVm.IsLiveActive    = (tabIndex == 2);
        }

        // ── Keyboard ──────────────────────────────────────────────────────

        private readonly HashSet<Keys> _heldModifiers = new();

        private void OnKeyDownReceived(Keys key)
        {
            if (MouseButtonCodeHelper.IsModifier(key))
                _heldModifiers.Add(key);
        }

        private void OnKeyReceived(Keys key)
        {
            if (!MouseButtonCodeHelper.IsModifier(key))
            {
                bool ctrl  = _heldModifiers.Contains(Keys.ControlKey) || _heldModifiers.Contains(Keys.LControlKey) || _heldModifiers.Contains(Keys.RControlKey);
                bool shift = _heldModifiers.Contains(Keys.ShiftKey)   || _heldModifiers.Contains(Keys.LShiftKey)   || _heldModifiers.Contains(Keys.RShiftKey);
                bool alt   = _heldModifiers.Contains(Keys.Menu)       || _heldModifiers.Contains(Keys.LMenu)       || _heldModifiers.Contains(Keys.RMenu);

                int code = MouseButtonCodeHelper.EncodeKeyboard(key, ctrl, shift, alt);
                if (_mouseStatisticsService.IsRegistered(code))
                    _mouseStatisticsService.TrackButtonClick(code, MouseButtonCodeHelper.FormatShortcut(code));
            }

            if (MouseButtonCodeHelper.IsModifier(key))
                _heldModifiers.Remove(key);

            _savingClickService.SaveClick(key);

            // Publish to live bus (only has subscribers when keyboard tab is open)
            _liveBus.PublishKey(key.ToString());
        }

        // ── Mouse ─────────────────────────────────────────────────────────

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
            _liveBus.PublishMouseButton(buttonCode);
        }

        private void OnMouseScrolled(int notches)
        {
            _mouseStatisticsService.TrackScroll(notches);
            _liveBus.PublishMouseScroll(notches);
        }

        // ── Refresh ───────────────────────────────────────────────────────

        private async void ExecuteRefreshData(object parameter)
        {
            await StatisticsVm.LoadStatsAsync();
            await KeyboardVm.LoadKeyCountsAsync();
            await MouseVm.LoadDataAsync();
        }

        // ── INotifyPropertyChanged ────────────────────────────────────────

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private string _title = "ClickStat";
        public string Title
        {
            get => _title;
            set { if (_title != value) { _title = value; OnPropertyChanged(); } }
        }
    }

    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Func<object, bool>? _canExecute;

        public RelayCommand(Action<object> execute, Func<object, bool>? canExecute = null)
        {
            _execute    = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter!) ?? true;
        public void Execute(object? parameter)    => _execute(parameter!);

        public event EventHandler? CanExecuteChanged
        {
            add    => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }
}
