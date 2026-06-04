using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ClickStat.Core.Helpers;
using ClickStat.Core.Interfaces;
using ClickStat.Core.Services;
using ClickStat.Infrastructure.Data;
using ClickStat.Presentation.Services;
using System.Windows.Forms;

namespace ClickStat.Presentation.ViewModels
{
    public enum AppPage
    {
        Overview, Keyboard, Mouse, Activity, Words, Apps, Settings
    }

    public class NavItem
    {
        public string  Icon  { get; init; } = "";
        public string  Label { get; init; } = "";
        public AppPage Page  { get; init; }
    }

    public class MainViewModel : INotifyPropertyChanged
    {
        // ── Services ────────────────────────────────────────────────────────
        private readonly IInputMonitorService    _inputMonitorService;
        private readonly IMouseMonitorService    _mouseMonitorService;
        private readonly ISavingClick            _savingClickService;
        private readonly IMouseStatisticsService _mouseStatisticsService;
        private readonly IStartupService         _startupService;
        private readonly LiveEventBus            _liveBus;
        private readonly WordProcessor           _wordProcessor;
        private readonly HourlyActivityProcessor _hourlyProcessor;
        private readonly AppUsageProcessor       _appUsageProcessor;
        private readonly BreakReminderService    _breakReminder;

        // ── ViewModels ──────────────────────────────────────────────────────
        public OverviewViewModel  OverviewVm  { get; }
        public KeyboardViewModel  KeyboardVm  { get; }
        public MouseViewModel     MouseVm     { get; }
        public ActivityViewModel  ActivityVm  { get; }
        public WordsViewModel     WordsVm     { get; }
        public AppsViewModel      AppsVm      { get; }
        public SettingsViewModel  SettingsVm  { get; }

        // ── Navigation ──────────────────────────────────────────────────────
        public ObservableCollection<NavItem> NavItems { get; } = new()
        {
            new NavItem { Icon = "📊", Label = "Обзор",        Page = AppPage.Overview  },
            new NavItem { Icon = "⌨️", Label = "Клавиатура",   Page = AppPage.Keyboard  },
            new NavItem { Icon = "🖱️", Label = "Мышь",         Page = AppPage.Mouse     },
            new NavItem { Icon = "📈", Label = "Активность",   Page = AppPage.Activity  },
            new NavItem { Icon = "📝", Label = "Слова",        Page = AppPage.Words     },
            new NavItem { Icon = "💻", Label = "Приложения",   Page = AppPage.Apps      },
            new NavItem { Icon = "⚙️", Label = "Настройки",    Page = AppPage.Settings  },
        };

        private AppPage _activePage = AppPage.Overview;
        public AppPage ActivePage
        {
            get => _activePage;
            set
            {
                if (_activePage == value) return;
                _activePage = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CurrentPageVm));
                UpdateLiveMode(value);
            }
        }

        public object CurrentPageVm => ActivePage switch
        {
            AppPage.Overview  => OverviewVm,
            AppPage.Keyboard  => KeyboardVm,
            AppPage.Mouse     => MouseVm,
            AppPage.Activity  => ActivityVm,
            AppPage.Words     => WordsVm,
            AppPage.Apps      => AppsVm,
            AppPage.Settings  => SettingsVm,
            _                 => OverviewVm
        };

        // ── Startup ─────────────────────────────────────────────────────────
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

        public ICommand RefreshCommand  { get; }
        public ICommand NavigateCommand { get; }

        // ── Constructor ─────────────────────────────────────────────────────
        public MainViewModel(
            IInputMonitorService    inputMonitorService,
            IMouseMonitorService    mouseMonitorService,
            ISavingClick            savingClickService,
            IMouseStatisticsService mouseStatisticsService,
            IStartupService         startupService,
            LiveEventBus            liveBus,
            WordProcessor           wordProcessor,
            HourlyActivityProcessor hourlyProcessor,
            AppUsageProcessor       appUsageProcessor,
            BreakReminderService    breakReminder,
            OverviewViewModel       overviewVm,
            KeyboardViewModel       keyboardVm,
            MouseViewModel          mouseVm,
            ActivityViewModel       activityVm,
            WordsViewModel          wordsVm,
            AppsViewModel           appsVm,
            SettingsViewModel       settingsVm)
        {
            _inputMonitorService    = inputMonitorService;
            _mouseMonitorService    = mouseMonitorService;
            _savingClickService     = savingClickService;
            _mouseStatisticsService = mouseStatisticsService;
            _startupService         = startupService;
            _liveBus                = liveBus;
            _wordProcessor          = wordProcessor;
            _hourlyProcessor        = hourlyProcessor;
            _appUsageProcessor      = appUsageProcessor;
            _breakReminder          = breakReminder;

            OverviewVm = overviewVm;
            KeyboardVm = keyboardVm;
            MouseVm    = mouseVm;
            ActivityVm = activityVm;
            WordsVm    = wordsVm;
            AppsVm     = appsVm;
            SettingsVm = settingsVm;

            // Pass break reminder to settings so user can configure it
            SettingsVm.BreakReminder = _breakReminder;

            _inputMonitorService.OnKeyDown   += OnKeyDownReceived;
            _inputMonitorService.OnKeyAction += OnKeyReceived;
            _inputMonitorService.StartMonitoring();

            _mouseMonitorService.OnButtonPressed += OnMouseButtonPressed;
            _mouseMonitorService.OnScroll        += OnMouseScrolled;
            _mouseMonitorService.OnMoved         += (dx, dy) => _mouseStatisticsService.TrackMovement(dx, dy);
            _mouseMonitorService.StartMonitoring();

            _breakReminder.ReminderTriggered += OnBreakReminder;

            RefreshCommand  = new RelayCommand(ExecuteRefresh);
            NavigateCommand = new RelayCommand(p => { if (p is AppPage page) NavigateTo(page); });
            _isInStartup   = _startupService.IsInStartup();
        }

        // ── Navigation ───────────────────────────────────────────────────────

        public void NavigateTo(AppPage page) => ActivePage = page;

        private void UpdateLiveMode(AppPage page)
        {
            OverviewVm.IsLiveActive = (page == AppPage.Overview);
            KeyboardVm.IsLiveActive = (page == AppPage.Keyboard);
            MouseVm.IsLiveActive    = (page == AppPage.Mouse);

            // Flush buffer → load fresh DB data. Order matters: flush first, then read.
            switch (page)
            {
                case AppPage.Overview:
                    _ = FlushThenLoad(_savingClickService.FlushAsync, OverviewVm.LoadStatsAsync);
                    break;
                case AppPage.Keyboard:
                    _ = FlushThenLoad(_savingClickService.FlushAsync, KeyboardVm.LoadKeyCountsAsync);
                    break;
                case AppPage.Mouse:
                    _ = FlushThenLoad(_mouseStatisticsService.FlushAsync, MouseVm.LoadDataAsync);
                    break;
                case AppPage.Activity:
                    _ = ActivityVm.LoadAsync();
                    break;
                case AppPage.Words:
                    _ = WordsVm.LoadAsync();
                    break;
                case AppPage.Apps:
                    _ = AppsVm.LoadAsync();
                    break;
            }
        }

        private static async Task FlushThenLoad(Func<Task> flush, Func<Task> load)
        {
            await flush();
            await load();
        }

        // ── Keyboard events ──────────────────────────────────────────────────

        private readonly HashSet<Keys> _heldModifiers = new();

        private void OnKeyDownReceived(Keys key)
        {
            if (MouseButtonCodeHelper.IsModifier(key))
                _heldModifiers.Add(key);
        }

        private void OnKeyReceived(Keys key)
        {
            bool ctrl  = _heldModifiers.Contains(Keys.ControlKey) || _heldModifiers.Contains(Keys.LControlKey) || _heldModifiers.Contains(Keys.RControlKey);
            bool shift = _heldModifiers.Contains(Keys.ShiftKey)   || _heldModifiers.Contains(Keys.LShiftKey)   || _heldModifiers.Contains(Keys.RShiftKey);
            bool alt   = _heldModifiers.Contains(Keys.Menu)       || _heldModifiers.Contains(Keys.LMenu)       || _heldModifiers.Contains(Keys.RMenu);

            // Keyboard-mapped mouse button detection
            if (!MouseButtonCodeHelper.IsModifier(key))
            {
                int code = MouseButtonCodeHelper.EncodeKeyboard(key, ctrl, shift, alt);
                if (_mouseStatisticsService.IsRegistered(code))
                    _mouseStatisticsService.TrackButtonClick(code, MouseButtonCodeHelper.FormatShortcut(code));
            }

            if (MouseButtonCodeHelper.IsModifier(key))
                _heldModifiers.Remove(key);

            _savingClickService.SaveClick(key);
            _liveBus.PublishKey(key.ToString());
            _hourlyProcessor.Record();
            _appUsageProcessor.RecordKey();
            _breakReminder.RecordActivity();

            // Word processing: only when no Ctrl/Alt (pure typing)
            if (!ctrl && !alt)
                _wordProcessor.ProcessKey(key);
            else
                _wordProcessor.ClearBuffer();
        }

        // ── Mouse events ─────────────────────────────────────────────────────

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
                _mouseStatisticsService.TrackClickPosition();
            }
            _appUsageProcessor.RecordClick();
            _liveBus.PublishMouseButton(buttonCode);
        }

        private void OnMouseScrolled(int notches)
        {
            _mouseStatisticsService.TrackScroll(notches);
            _liveBus.PublishMouseScroll(notches);
        }

        // ── Break reminder ────────────────────────────────────────────────────

        private void OnBreakReminder(int minutes)
        {
            System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                System.Windows.MessageBox.Show(
                    $"Ты работаешь {minutes} минут без перерыва. Сделай паузу! 🧘",
                    "ClickStat — Напоминание о перерыве",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            });
        }

        // ── Refresh ───────────────────────────────────────────────────────────

        private async void ExecuteRefresh(object _)
        {
            await OverviewVm.LoadStatsAsync();
            await KeyboardVm.LoadKeyCountsAsync();
            await MouseVm.LoadDataAsync();
        }

        // ── INotifyPropertyChanged ────────────────────────────────────────────
        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? p = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));

        public string Title => "ClickStat";
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
        public bool CanExecute(object? p)  => _canExecute?.Invoke(p!) ?? true;
        public void Execute(object? p)     => _execute(p!);
        public event EventHandler? CanExecuteChanged
        {
            add    => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }
}
