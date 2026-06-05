using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ClickStat.Presentation.Converters;

public class IntensityToColorConverter : IValueConverter
{
    public static readonly IntensityToColorConverter Instance = new();

    // Dark → purple → violet → bright (GitHub-style, purple theme)
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double intensity = value is double d ? d : 0;

        return intensity switch
        {
            0           => Color.FromRgb(0x0E, 0x0E, 0x1C),
            < 0.2       => Color.FromRgb(0x1E, 0x0E, 0x3A),
            < 0.4       => Color.FromRgb(0x3A, 0x1E, 0x6E),
            < 0.6       => Color.FromRgb(0x6A, 0x2E, 0xAA),
            < 0.8       => Color.FromRgb(0x99, 0x50, 0xDF),
            _           => Color.FromRgb(0xCC, 0x88, 0xFF),
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
