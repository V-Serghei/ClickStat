using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using ClickStat.Infrastructure.Data;

namespace ClickStat.Presentation.Views;

public partial class InputTemplatePickerDialog : Window, INotifyPropertyChanged
{
    private readonly InputTemplateProcessor _templateProcessor;
    private CancellationTokenSource? _searchDebounceCts;
    private int _loadVersion;

    public ObservableCollection<InputTemplateItem> Templates { get; } = new();

    public string? SelectedText { get; private set; }
    public bool ShouldPaste { get; private set; }

    public InputTemplatePickerDialog(InputTemplateProcessor templateProcessor)
    {
        _templateProcessor = templateProcessor;
        InitializeComponent();
        DataContext = this;
        AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(ClearButtonFocusAfterClick), true);
        Loaded += OnLoaded;
    }

    private void ClearButtonFocusAfterClick(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(() => Keyboard.ClearFocus(), DispatcherPriority.Background);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await LoadTemplatesAsync();
        SearchTextBox.Focus();
    }

    private async Task LoadTemplatesAsync()
    {
        var version = Interlocked.Increment(ref _loadVersion);
        var entries = await _templateProcessor.SearchAsync(SearchTextBox.Text);
        if (version != _loadVersion)
            return;

        Templates.Clear();
        foreach (var entry in entries)
            Templates.Add(new InputTemplateItem(entry));

        EmptyState.Visibility = Templates.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        OnPropertyChanged(nameof(Templates));
    }

    private async void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded)
            return;

        _searchDebounceCts?.Cancel();
        var cts = new CancellationTokenSource();
        _searchDebounceCts = cts;

        try
        {
            await Task.Delay(180, cts.Token);
            await LoadTemplatesAsync();
        }
        catch (TaskCanceledException)
        {
        }
    }

    private void PasteButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not InputTemplateItem item)
            return;

        SelectedText = item.Text;
        ShouldPaste = true;
        DialogResult = true;
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not InputTemplateItem item)
            return;

        System.Windows.Clipboard.SetText(item.Text);
        StatusText.Text = "Скопировано";
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not InputTemplateItem item)
            return;

        await _templateProcessor.DeleteAsync(item.Id);
        Templates.Remove(item);
        EmptyState.Visibility = Templates.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = "Удалено";
    }

    private void ToggleExpandButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not InputTemplateItem item)
            return;

        item.IsExpanded = !item.IsExpanded;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    protected override void OnClosed(EventArgs e)
    {
        _searchDebounceCts?.Cancel();
        _searchDebounceCts?.Dispose();
        base.OnClosed(e);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class InputTemplateItem : INotifyPropertyChanged
{
    public int Id { get; }
    public string Title { get; }
    public string Text { get; }
    public string Preview { get; }
    public string CreatedAtLabel { get; }

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayText));
            OnPropertyChanged(nameof(TextMaxHeight));
            OnPropertyChanged(nameof(ExpandIcon));
        }
    }

    public string DisplayText => IsExpanded ? Text : Preview;
    public double TextMaxHeight => IsExpanded ? 220 : 34;
    public string ExpandIcon => IsExpanded ? "\uE73F" : "\uE740";

    public InputTemplateItem(InputTemplateEntry entry)
    {
        Id = entry.Id;
        Title = entry.Title;
        Text = entry.Text;
        Preview = MakePreview(entry.Text);
        CreatedAtLabel = DateTimeOffset.TryParse(entry.CreatedAt, out var createdAt)
            ? createdAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm")
            : entry.CreatedAt;
    }

    private static string MakePreview(string text)
    {
        var normalized = text.Replace("\r", "").Replace("\n", " ");
        return normalized.Length <= 120 ? normalized : normalized[..120] + "...";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
