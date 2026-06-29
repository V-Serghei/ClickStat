using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Input;
using ClickStat.Core.Helpers;
using ClickStat.Core.Interfaces;
using ClickStat.Core.Services;
using ClickStat.Infrastructure.Data;
using ClickStat.Presentation.Services;
using ClickStat.Presentation.Views;
using System.Windows.Forms;

namespace ClickStat.Presentation.ViewModels
{
    public enum AppPage
    {
        Overview, Keyboard, Mouse, Activity, Words, Apps, Gamepads, Settings
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
        private readonly InputTemplateProcessor  _inputTemplateProcessor;
        private readonly BreakReminderService    _breakReminder;
        private bool _isTemplatePickerOpen;
        private InputTemplatePickerDialog? _templatePickerDialog;
        private bool _isSelectionCaptureInProgress;

        // ── ViewModels ──────────────────────────────────────────────────────
        public OverviewViewModel  OverviewVm  { get; }
        public KeyboardViewModel  KeyboardVm  { get; }
        public MouseViewModel     MouseVm     { get; }
        public ActivityViewModel  ActivityVm  { get; }
        public WordsViewModel     WordsVm     { get; }
        public AppsViewModel      AppsVm      { get; }
        public GamepadsViewModel  GamepadsVm  { get; }
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
            new NavItem { Icon = "🎮", Label = "Геймпады",     Page = AppPage.Gamepads  },
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
            AppPage.Gamepads  => GamepadsVm,
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
            InputTemplateProcessor  inputTemplateProcessor,
            BreakReminderService    breakReminder,
            OverviewViewModel       overviewVm,
            KeyboardViewModel       keyboardVm,
            MouseViewModel          mouseVm,
            ActivityViewModel       activityVm,
            WordsViewModel          wordsVm,
            AppsViewModel           appsVm,
            GamepadsViewModel       gamepadsVm,
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
            _inputTemplateProcessor = inputTemplateProcessor;
            _breakReminder          = breakReminder;

            OverviewVm = overviewVm;
            KeyboardVm = keyboardVm;
            MouseVm    = mouseVm;
            ActivityVm = activityVm;
            WordsVm    = wordsVm;
            AppsVm     = appsVm;
            GamepadsVm = gamepadsVm;
            SettingsVm = settingsVm;

            // Pass break reminder to settings so user can configure it
            SettingsVm.BreakReminder = _breakReminder;

            _inputMonitorService.OnKeyDown   += OnKeyDownReceived;
            _inputMonitorService.OnKeyAction += OnKeyReceived;
            _inputMonitorService.OnKeyUp     += OnKeyUpReceived;
            _inputMonitorService.StartMonitoring();

            _mouseMonitorService.OnButtonPressed += OnMouseButtonPressed;
            _mouseMonitorService.OnScroll        += OnMouseScrolled;
            _mouseMonitorService.OnMoved         += (dx, dy) => _mouseStatisticsService.TrackMovement(dx, dy);
            _mouseMonitorService.StartMonitoring();

            _breakReminder.ReminderTriggered += OnBreakReminder;

            RefreshCommand  = new RelayCommand(ExecuteRefresh);
            NavigateCommand = new RelayCommand(p => { if (p is AppPage page) NavigateTo(page); });
            _isInStartup   = _startupService.IsInStartup();
            UpdateLiveMode(_activePage);
        }

        // ── Navigation ───────────────────────────────────────────────────────

        public void NavigateTo(AppPage page) => ActivePage = page;

        private void UpdateLiveMode(AppPage page)
        {
            OverviewVm.IsLiveActive = (page == AppPage.Overview);
            KeyboardVm.IsLiveActive = (page == AppPage.Keyboard);
            MouseVm.IsLiveActive    = (page == AppPage.Mouse);
            if (page != AppPage.Gamepads)
                GamepadsVm.StopMonitoring();

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
                    _ = FlushThenLoad(_wordProcessor.FlushAsync, WordsVm.LoadAsync);
                    break;
                case AppPage.Apps:
                    _ = AppsVm.LoadAsync();
                    break;
                case AppPage.Gamepads:
                    _ = GamepadsVm.LoadAsync();
                    break;
            }
        }

        private static async Task FlushThenLoad(Func<Task> flush, Func<Task> load)
        {
            try
            {
                await flush();
                await load();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Flush/load failed: {ex.Message}");
            }
        }

        // ── Keyboard events ──────────────────────────────────────────────────

        private readonly HashSet<Keys> _heldModifiers = new();

        private void OnKeyDownReceived(Keys key)
        {
            if (MouseButtonCodeHelper.IsModifier(key))
            {
                _heldModifiers.Add(key);
                if (IsCtrlHeld() || IsAltHeld())
                    _wordProcessor.ClearBuffer();
                return;
            }

            if (IsCtrlHeld() || IsAltHeld())
            {
                _wordProcessor.ClearBuffer();
                return;
            }

            _wordProcessor.ProcessKey(key, IsShiftHeld());
        }

        private void OnKeyReceived(Keys key)
        {
            bool ctrl  = IsCtrlHeld();
            bool shift = IsShiftHeld();
            bool alt   = IsAltHeld();

            // Keyboard-mapped mouse button detection
            if (!MouseButtonCodeHelper.IsModifier(key))
            {
                int code = MouseButtonCodeHelper.EncodeKeyboard(key, ctrl, shift, alt);
                if (_mouseStatisticsService.IsRegistered(code))
                    _mouseStatisticsService.TrackButtonClick(code, MouseButtonCodeHelper.FormatShortcut(code));
            }

            _savingClickService.SaveClick(key);
            _liveBus.PublishKey(key.ToString());
            if (!MouseButtonCodeHelper.IsModifier(key) && !ctrl && !alt)
                KeyboardVm.RecordSessionKey(key, shift);
            _hourlyProcessor.Record();
            _appUsageProcessor.RecordKey();
            _breakReminder.RecordActivity();
        }

        public Task ToggleInputTemplatePickerAsync(IntPtr targetWindow) =>
            ShowInputTemplatePickerAsync(targetWindow);

        public Task CaptureSelectedTextAsTemplateFromHotkeyAsync(IntPtr targetWindow) =>
            CaptureSelectedTextAsTemplateAsync(targetWindow);

        private async Task ShowInputTemplatePickerAsync(IntPtr targetWindow)
        {
            if (_isTemplatePickerOpen)
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    _templatePickerDialog?.Close());
                return;
            }

            _isTemplatePickerOpen = true;
            try
            {
                string? selectedText = null;
                var shouldPaste = false;

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var dialog = new InputTemplatePickerDialog(_inputTemplateProcessor);
                    _templatePickerDialog = dialog;
                    var result = dialog.ShowDialog();
                    if (result == true && dialog.ShouldPaste)
                    {
                        selectedText = dialog.SelectedText;
                        shouldPaste = true;
                    }
                });

                if (shouldPaste && !string.IsNullOrEmpty(selectedText))
                    await PasteTextIntoTargetAsync(selectedText, targetWindow);
            }
            finally
            {
                _templatePickerDialog = null;
                _isTemplatePickerOpen = false;
            }
        }

        private static async Task PasteTextIntoTargetAsync(string text, IntPtr targetWindow)
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                System.Windows.Clipboard.SetText(text));

            await Task.Delay(120);
            if (targetWindow != IntPtr.Zero)
                SetForegroundWindow(targetWindow);

            await Task.Delay(80);
            SendKeys.SendWait("^v");
        }

        private async Task CaptureSelectedTextAsTemplateAsync(IntPtr targetWindow)
        {
            if (_isSelectionCaptureInProgress)
                return;

            _isSelectionCaptureInProgress = true;
            System.Windows.IDataObject? previousClipboard = null;

            try
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (System.Windows.Clipboard.ContainsData(System.Windows.DataFormats.Text)
                        || System.Windows.Clipboard.ContainsData(System.Windows.DataFormats.UnicodeText)
                        || System.Windows.Clipboard.ContainsData(System.Windows.DataFormats.Bitmap)
                        || System.Windows.Clipboard.ContainsData(System.Windows.DataFormats.FileDrop))
                    {
                        previousClipboard = System.Windows.Clipboard.GetDataObject();
                    }
                });

                await WaitForHotkeyReleaseAsync();

                if (targetWindow != IntPtr.Zero)
                    SetForegroundWindow(targetWindow);

                await Task.Delay(80);
                SendKeys.SendWait("^c");
                await Task.Delay(180);

                string? selectedText = null;
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (System.Windows.Clipboard.ContainsText())
                        selectedText = System.Windows.Clipboard.GetText();
                });

                if (!string.IsNullOrWhiteSpace(selectedText))
                    await _inputTemplateProcessor.SaveAsync(selectedText);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Capture selected text failed: {ex.Message}");
            }
            finally
            {
                if (previousClipboard != null)
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        System.Windows.Clipboard.SetDataObject(previousClipboard, true));
                }

                _isSelectionCaptureInProgress = false;
            }
        }

        private static async Task WaitForHotkeyReleaseAsync()
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(900);
            while (DateTime.UtcNow < deadline
                   && (IsPhysicalKeyDown(Keys.D)
                       || IsPhysicalKeyDown(Keys.ControlKey)
                       || IsPhysicalKeyDown(Keys.ShiftKey)
                       || IsPhysicalKeyDown(Keys.Menu)))
            {
                await Task.Delay(20);
            }
        }

        private void OnKeyUpReceived(Keys key)
        {
            if (MouseButtonCodeHelper.IsModifier(key))
                _heldModifiers.Remove(key);
        }

        private bool IsCtrlHeld() =>
            _heldModifiers.Contains(Keys.ControlKey) || _heldModifiers.Contains(Keys.LControlKey) || _heldModifiers.Contains(Keys.RControlKey);

        private bool IsShiftHeld() =>
            _heldModifiers.Contains(Keys.ShiftKey) || _heldModifiers.Contains(Keys.LShiftKey) || _heldModifiers.Contains(Keys.RShiftKey);

        private bool IsAltHeld() =>
            _heldModifiers.Contains(Keys.Menu) || _heldModifiers.Contains(Keys.LMenu) || _heldModifiers.Contains(Keys.RMenu);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private static bool IsPhysicalKeyDown(Keys key) =>
            (GetAsyncKeyState((int)(key & Keys.KeyCode)) & 0x8000) != 0;

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

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
            await _savingClickService.FlushAsync();
            await _mouseStatisticsService.FlushAsync();
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
