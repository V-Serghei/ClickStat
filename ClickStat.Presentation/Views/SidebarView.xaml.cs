using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using ClickStat.Presentation.Services;
using ClickStat.Presentation.ViewModels;

namespace ClickStat.Presentation.Views;

public partial class SidebarView : UserControl
{
    private MainViewModel? _vm;
    private readonly System.Collections.Generic.List<Border> _navBorders = new();

    public SidebarView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        LocalizationService.Instance.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == "Item[]")
                BuildNavButtons();
        };
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not MainViewModel vm) return;
        _vm = vm;
        _vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.ActivePage))
                UpdateSelectedState();
        };
        BuildNavButtons();
    }

    private void BuildNavButtons()
    {
        if (_vm == null) return;
        NavPanel.Children.Clear();
        _navBorders.Clear();

        foreach (var item in _vm.NavItems)
        {
            var border = new Border
            {
                CornerRadius = new CornerRadius(8),
                Margin       = new Thickness(6, 2, 6, 2),
                Padding      = new Thickness(0),
                Cursor       = System.Windows.Input.Cursors.Hand
            };

            var icon = new TextBlock
            {
                Text     = item.Icon,
                FontSize = 17,
                Margin   = new Thickness(14, 11, 10, 11),
                VerticalAlignment = VerticalAlignment.Center
            };

            var label = new TextBlock
            {
                Text       = item.Label,
                FontSize   = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x58, 0x58, 0xA0)),
                VerticalAlignment = VerticalAlignment.Center
            };

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(icon);
            row.Children.Add(label);
            border.Child = row;

            var page = item.Page;
            border.MouseLeftButtonDown += (_, _) => _vm?.NavigateTo(page);
            border.MouseEnter += (_, _) =>
            {
                if (border.Tag is not bool selected || !(bool)border.Tag)
                    border.Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x3A));
            };
            border.MouseLeave += (_, _) =>
            {
                if (border.Tag is not bool selected || !(bool)border.Tag)
                    border.Background = Brushes.Transparent;
            };

            _navBorders.Add(border);
            NavPanel.Children.Add(border);
        }

        UpdateSelectedState();
    }

    private void UpdateSelectedState()
    {
        if (_vm == null) return;
        for (int i = 0; i < _navBorders.Count && i < _vm.NavItems.Count; i++)
        {
            bool isSelected = _vm.NavItems[i].Page == _vm.ActivePage;
            var border = _navBorders[i];
            border.Tag = isSelected;

            if (isSelected)
            {
                border.Background   = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x4A));
                border.BorderBrush  = new SolidColorBrush(Color.FromRgb(0xAA, 0x70, 0xFF));
                border.BorderThickness = new Thickness(0, 0, 3, 0);
                if (border.Child is StackPanel sp && sp.Children[1] is TextBlock lbl)
                    lbl.Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xAA, 0xFF));
            }
            else
            {
                border.Background      = Brushes.Transparent;
                border.BorderBrush     = Brushes.Transparent;
                border.BorderThickness = new Thickness(0);
                if (border.Child is StackPanel sp && sp.Children[1] is TextBlock lbl)
                    lbl.Foreground = new SolidColorBrush(Color.FromRgb(0x58, 0x58, 0xA0));
            }
        }
    }
}
