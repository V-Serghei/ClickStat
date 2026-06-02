using System;
using System.Windows.Forms;
using ClickStat.Core.Interfaces;
using ClickStat.Core.Models;
using ClickStat.Infrastructure.InputMonitoring;

namespace ClickStat.Core.Services
{
    public class InputMonitorService : IInputMonitorService
    {
        private readonly KeyboardMonitor _keyboardMonitor;

        public event Action<Keys>? OnKeyAction;
        public event Action<Keys>? OnKeyDown;

        public InputMonitorService()
        {
            _keyboardMonitor = new KeyboardMonitor();
            _keyboardMonitor.KeyPressed += key => OnKeyAction?.Invoke(key);
            _keyboardMonitor.KeyDown    += key => OnKeyDown?.Invoke(key);
        }

        public void StartMonitoring()
        {
            _keyboardMonitor.Subscribe();
        }

        public void StopMonitoring()
        {
            _keyboardMonitor.Unsubscribe();
        }

        public void ResetStatistics()
        {
        }
    }
}