using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using ClickStat.Presentation.Services;
using ClickStat.Presentation.ViewModels;

namespace ClickStat.Presentation;

public partial class MainWindow : Window
{
    private const int HotkeyShowTemplates = 0x434B01;
    private const int HotkeyCaptureSelection = 0x434B02;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModNoRepeat = 0x4000;
    private const uint VkD = 0x44;
    private const int WmHotkey = 0x0312;

    private HwndSource? _source;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(ClearButtonFocusAfterClick), true);
        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;
    }

    private void ClearButtonFocusAfterClick(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(() => Keyboard.ClearFocus(), DispatcherPriority.Background);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseButton_Click(object sender, RoutedEventArgs e)
        => Close();

    private void UiLanguageItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string languageCode })
            LocalizationService.Instance.SetLanguage(languageCode);

        UiLanguagePopup.IsOpen = false;
    }


    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        _source = HwndSource.FromHwnd(hwnd);
        _source?.AddHook(WndProc);

        RegisterHotKey(hwnd, HotkeyShowTemplates, ModAlt | ModNoRepeat, VkD);
        RegisterHotKey(hwnd, HotkeyCaptureSelection, ModControl | ModAlt | ModShift | ModNoRepeat, VkD);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        UnregisterHotKey(hwnd, HotkeyShowTemplates);
        UnregisterHotKey(hwnd, HotkeyCaptureSelection);
        _source?.RemoveHook(WndProc);
        _source = null;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmHotkey || DataContext is not MainViewModel viewModel)
            return IntPtr.Zero;

        var targetWindow = GetForegroundWindow();
        var id = wParam.ToInt32();
        if (id == HotkeyShowTemplates)
        {
            _ = viewModel.ToggleInputTemplatePickerAsync(targetWindow);
            handled = true;
        }
        else if (id == HotkeyCaptureSelection)
        {
            _ = viewModel.CaptureSelectedTextAsTemplateFromHotkeyAsync(targetWindow);
            handled = true;
        }

        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
