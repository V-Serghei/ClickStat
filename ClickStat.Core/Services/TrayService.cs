using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using ClickStat.Core.Interfaces;
using Application = System.Windows.Application;

namespace ClickStat.Core.Services;

public class TrayService : ITrayService
{
    
    private NotifyIcon? _notifyIcon;
    private Window? _mainWindow;

    public void Initialize(Window window)
    {
        _mainWindow = window;
        _mainWindow.Closing += MainWindow_Closing;
        _notifyIcon = new NotifyIcon
        {
            Icon = new Icon(Application.GetResourceStream(new Uri("pack://application:,,,/icon.ico")).Stream),
            Text = "ClickStat - Input statistics",
            Visible = false
        };
        _notifyIcon.ContextMenuStrip = new ContextMenuStrip();
        _notifyIcon.ContextMenuStrip.Items.Add("Show", null, OnShowClick);
        _notifyIcon.ContextMenuStrip.Items.Add("Exit", null, OnExitClick);

        _notifyIcon.DoubleClick += (s, e) => OnShowClick(s, e);
    }
    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;

        _mainWindow?.Hide();
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = true;
        }
    }

    private void OnShowClick(object? sender, EventArgs e)
    {
        if (_mainWindow == null) return;
            
        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
            
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
        }
    }

    private void OnExitClick(object? sender, EventArgs e)
    {
        Application.Current.Shutdown();
    }
}
