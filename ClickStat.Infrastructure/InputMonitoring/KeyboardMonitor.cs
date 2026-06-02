using System;
using System.Windows.Forms;
using Gma.System.MouseKeyHook;

namespace ClickStat.Infrastructure.InputMonitoring
{
    public class KeyboardMonitor : IDisposable
    {
        private readonly IKeyboardMouseEvents _globalHook;
        private readonly HashSet<Keys> _pressedKeys = new HashSet<Keys>();

        public event Action<Keys> KeyPressed;  // KeyUp
        public event Action<Keys> KeyDown;     // KeyDown

        public KeyboardMonitor()
        {
            _globalHook = Hook.GlobalEvents();
        }

        public void Subscribe()
        {
            _globalHook.KeyDown += OnKeyDown;
            _globalHook.KeyUp += OnKeyUp;
        }

        public void Unsubscribe()
        {
            _globalHook.KeyDown -= OnKeyDown;
            _globalHook.KeyUp -= OnKeyUp;
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (_pressedKeys.Add(e.KeyCode))
                KeyDown?.Invoke(e.KeyCode);
        }

        private void OnKeyUp(object sender, KeyEventArgs e)
        {
            if (_pressedKeys.Remove(e.KeyCode)) 
            {
                KeyPressed?.Invoke(e.KeyCode); 
            }
        }

        public void Dispose()
        {
            Unsubscribe();
            _globalHook.Dispose();
        }
    }
}