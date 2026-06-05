using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using ClickStat.Core.Helpers;
using Gma.System.MouseKeyHook;

namespace ClickStat.Presentation.Views;

public partial class AddMouseButtonDialog : Window
{
    public (int code, string name)? RegisteredButton { get; private set; }

    private IKeyboardMouseEvents? _hook;
    private bool _listening;
    private int    _capturedCode;
    private string _capturedName = string.Empty;

    // Track held modifier keys during capture phase
    private readonly HashSet<Keys> _held = new();

    public AddMouseButtonDialog()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        for (int i = 3; i >= 1; i--)
        {
            CountdownLabel.Text = i.ToString();
            await Task.Delay(600);
        }
        CountdownLabel.Text = "▶";
        StartListening();
    }

    private void StartListening()
    {
        _listening = true;
        _hook = Hook.GlobalEvents();
        _hook.MouseDown  += OnMouseCaptured;
        _hook.KeyDown    += OnKeyDownCapture;
        _hook.KeyUp      += OnKeyUpCapture;
    }

    // ── Mouse button capture ────────────────────────────────────────────────

    private void OnMouseCaptured(object? sender, MouseEventArgs e)
    {
        if (!_listening) return;
        StopListening();

        _capturedCode = (int)e.Button;
        _capturedName = DefaultMouseName(e.Button);

        Dispatcher.Invoke(ShowNameEntryPhase);
    }

    private static string DefaultMouseName(MouseButtons button) => button switch
    {
        MouseButtons.Left     => "Левая кнопка",
        MouseButtons.Right    => "Правая кнопка",
        MouseButtons.Middle   => "Средняя кнопка",
        MouseButtons.XButton1 => "Кнопка назад",
        MouseButtons.XButton2 => "Кнопка вперёд",
        _                     => $"Кнопка мыши {(int)button}"
    };

    // ── Keyboard shortcut capture ───────────────────────────────────────────

    private void OnKeyDownCapture(object? sender, KeyEventArgs e)
    {
        if (!_listening) return;
        _held.Add(e.KeyCode);
    }

    private void OnKeyUpCapture(object? sender, KeyEventArgs e)
    {
        if (!_listening) return;
        if (MouseButtonCodeHelper.IsModifier(e.KeyCode))
        {
            _held.Remove(e.KeyCode);
            return;
        }

        // Non-modifier key released → this is the shortcut
        bool ctrl  = _held.Contains(Keys.ControlKey) || _held.Contains(Keys.LControlKey) || _held.Contains(Keys.RControlKey);
        bool shift = _held.Contains(Keys.ShiftKey)   || _held.Contains(Keys.LShiftKey)   || _held.Contains(Keys.RShiftKey);
        bool alt   = _held.Contains(Keys.Menu)       || _held.Contains(Keys.LMenu)       || _held.Contains(Keys.RMenu);

        // Require at least one modifier so plain letter-keys aren't accidentally captured
        if (!ctrl && !shift && !alt)
        {
            // Single key (e.g. F5, F9, etc.) — allow function keys and special keys without modifier
            bool isFunctionOrSpecial = (e.KeyCode >= Keys.F1 && e.KeyCode <= Keys.F24)
                || e.KeyCode is Keys.Insert or Keys.Delete or Keys.Home or Keys.End
                            or Keys.PageUp  or Keys.PageDown
                            or Keys.NumPad0 or Keys.NumPad1 or Keys.NumPad2 or Keys.NumPad3
                            or Keys.NumPad4 or Keys.NumPad5 or Keys.NumPad6 or Keys.NumPad7
                            or Keys.NumPad8 or Keys.NumPad9;
            if (!isFunctionOrSpecial) return;
        }

        StopListening();

        _capturedCode = MouseButtonCodeHelper.EncodeKeyboard(e.KeyCode, ctrl, shift, alt);
        _capturedName = MouseButtonCodeHelper.FormatShortcut(_capturedCode);

        Dispatcher.Invoke(ShowNameEntryPhase);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private void StopListening()
    {
        _listening = false;
        if (_hook == null) return;
        _hook.MouseDown -= OnMouseCaptured;
        _hook.KeyDown   -= OnKeyDownCapture;
        _hook.KeyUp     -= OnKeyUpCapture;
        _hook.Dispose();
        _hook = null;
    }

    private void ShowNameEntryPhase()
    {
        WaitingPanel.Visibility   = Visibility.Collapsed;
        NameEntryPanel.Visibility = Visibility.Visible;

        bool isKeyboard = MouseButtonCodeHelper.IsKeyboardMapped(_capturedCode);
        DetectedLabel.Text = isKeyboard
            ? $"Клавиатурное сочетание: {MouseButtonCodeHelper.FormatShortcut(_capturedCode)}"
            : $"Кнопка мыши: код {_capturedCode}";

        NameTextBox.Text  = _capturedName;
        OkButton.IsEnabled = true;
        NameTextBox.Focus();
        NameTextBox.SelectAll();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        var name = NameTextBox.Text.Trim();
        if (string.IsNullOrEmpty(name)) return;
        RegisteredButton = (_capturedCode, name);
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnClosed(object? sender, EventArgs e)
    {
        StopListening();
    }
}
