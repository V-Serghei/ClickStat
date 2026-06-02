using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClickStat.Presentation.Converters;
using ClickStat.Presentation.ViewModels;

namespace ClickStat.Presentation.Views;

public partial class ActivityView : UserControl
{
    private static readonly string[] DayLabels = { "Вс","Пн","Вт","Ср","Чт","Пт","Сб" };

    public ActivityView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is ActivityViewModel vm)
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(ActivityViewModel.HeatmapCells))
                    RenderHeatmap(vm);
            };
    }

    private void RenderHeatmap(ActivityViewModel vm)
    {
        HeatmapGrid.Children.Clear();
        HeatmapGrid.RowDefinitions.Clear();
        HeatmapGrid.ColumnDefinitions.Clear();

        // 7 rows (days) + 1 label col, 24 cols (hours)
        HeatmapGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) }); // day label
        for (int h = 0; h < 24; h++)
            HeatmapGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        for (int d = 0; d < 7; d++)
        {
            HeatmapGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });

            // Day label
            var lbl = new TextBlock
            {
                Text              = DayLabels[d],
                Foreground        = Brushes.DimGray,
                FontSize          = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(0, 0, 6, 0),
                TextAlignment     = TextAlignment.Right
            };
            Grid.SetRow(lbl, d);
            Grid.SetColumn(lbl, 0);
            HeatmapGrid.Children.Add(lbl);
        }

        var converter = IntensityToColorConverter.Instance;

        foreach (var cell in vm.HeatmapCells)
        {
            var color  = (Color)converter.Convert(cell.Intensity, typeof(Color), null!, System.Globalization.CultureInfo.InvariantCulture);
            var border = new Border
            {
                Background   = new SolidColorBrush(color),
                CornerRadius = new CornerRadius(3),
                Margin       = new Thickness(1),
                ToolTip      = cell.Tooltip
            };
            Grid.SetRow(border,    cell.DayOfWeek);
            Grid.SetColumn(border, cell.Hour + 1); // +1 for label column
            HeatmapGrid.Children.Add(border);
        }
    }
}
