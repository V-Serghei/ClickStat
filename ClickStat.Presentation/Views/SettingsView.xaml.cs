using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using ClickStat.Presentation.Services;
using ClickStat.Presentation.ViewModels;
using Microsoft.Win32;

namespace ClickStat.Presentation.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        LocalizationService.Instance.PropertyChanged += (_, args) =>
        {
            if ((args.PropertyName == "Item[]" || args.PropertyName == nameof(LocalizationService.CurrentLanguage))
                && DataContext is SettingsViewModel vm)
                RefreshPreview(vm);
        };
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is SettingsViewModel vm)
        {
            RefreshPreview(vm);
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName is nameof(SettingsViewModel.BackgroundUri)
                                     or nameof(SettingsViewModel.BackgroundPath))
                    RefreshPreview(vm);
            };
        }
    }

    private void RefreshPreview(SettingsViewModel vm)
    {
        try
        {
            string uri = vm.BackgroundUri;
            BgPreview.Source = string.IsNullOrEmpty(uri) ? null : new BitmapImage(new System.Uri(uri, System.UriKind.RelativeOrAbsolute));
            BgPathLabel.Text = string.IsNullOrEmpty(vm.BackgroundPath)
                ? LocalizationService.Instance["Settings.DefaultImage"]
                : vm.BackgroundPath;
        }
        catch { BgPreview.Source = null; }
    }

    private void PickImage_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title  = LocalizationService.Instance["Settings.PickImageDialogTitle"],
            Filter = LocalizationService.Instance["Settings.ImageFilter"]
        };
        if (dlg.ShowDialog() == true && DataContext is SettingsViewModel vm)
            vm.BackgroundPath = dlg.FileName;
    }

    private void ClearImage_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm) vm.ClearBackground();
    }
}
