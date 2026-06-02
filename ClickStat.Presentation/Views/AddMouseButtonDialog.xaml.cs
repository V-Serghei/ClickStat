using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using Gma.System.MouseKeyHook;

namespace ClickStat.Presentation.Views;

public partial class AddMouseButtonDialog : Window
{
    public (int code, string name)? RegisteredButton { get; private set; }

    private IKeyboardMouseEvents? _hook;
    private bool _listening;
    private int _capturedCode;
    private string _capturedName = string.Empty;

    public AddMouseButtonDialog()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Countdown so the click that opened this dialog isn't captured
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
        _hook.MouseDown += OnMouseCaptured;
    }

    private void OnMouseCaptured(object? sender, MouseEventArgs e)
    {
        if (!_listening) return;
        _listening = false;
        _hook!.MouseDown -= OnMouseCaptured;
        _hook.Dispose();
        _hook = null;

        _capturedCode = (int)e.Button;
        _capturedName = GetDefaultName(e.Button);

        Dispatcher.Invoke(ShowNameEntryPhase);
    }

    private static string GetDefaultName(MouseButtons button) => button switch
    {
        MouseButtons.Left     => "Левая кнопка",
        MouseButtons.Right    => "Правая кнопка",
        MouseButtons.Middle   => "Средняя кнопка",
        MouseButtons.XButton1 => "Кнопка назад",
        MouseButtons.XButton2 => "Кнопка вперёд",
        _                     => $"Кнопка {(int)button}"
    };

    private void ShowNameEntryPhase()
    {
        WaitingPanel.Visibility   = Visibility.Collapsed;
        NameEntryPanel.Visibility = Visibility.Visible;
        DetectedLabel.Text        = $"Определена кнопка: код {_capturedCode}";
        NameTextBox.Text          = _capturedName;
        OkButton.IsEnabled        = true;
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
        _listening = false;
        _hook?.Dispose();
    }
}
