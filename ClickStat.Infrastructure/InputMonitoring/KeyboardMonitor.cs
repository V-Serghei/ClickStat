using System;
using System.Windows.Forms;
using Gma.System.MouseKeyHook;

namespace ClickStat.Infrastructure.InputMonitoring
{
    public class KeyboardMonitor : IDisposable
    {
        private readonly IKeyboardMouseEvents _globalHook;
        
        public event KeyEventHandler KeyDown;

        public KeyboardMonitor()
        {
            _globalHook = Hook.GlobalEvents();
        }

        public void Subscribe()
        {
            _globalHook.KeyDown += OnKeyDown;
        }

        public void Unsubscribe()
        {
            _globalHook.KeyDown -= OnKeyDown;
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            KeyDown?.Invoke(this, e);
        }

        public void Dispose()
        {
            Unsubscribe();
            _globalHook.Dispose();
        }
    }
}