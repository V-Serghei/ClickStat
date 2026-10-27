using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ClickStat.Presentation.Converters;

public sealed class TrainingKeyBrushConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var keyName = values.Length > 0 ? values[0] as string : null;
        var counts = values.Length > 1 ? values[1] as Dictionary<string, int> : null;
        var activeKey = values.Length > 2 ? values[2] as string : null;
        var isError = values.Length > 3 && values[3] is bool error && error;

        if (!string.IsNullOrEmpty(keyName) && keyName == activeKey)
            return Brush(isError ? "#F56565" : "#48BB78");

        var count = 0;
        if (!string.IsNullOrEmpty(keyName))
            counts?.TryGetValue(keyName, out count);

        if (count <= 0)
            return Brush("#20203A");
        if (count < 3)
            return Brush("#4299E1");
        if (count < 8)
            return Brush("#48BB78");
        if (count < 15)
            return Brush("#E9D52D");
        return Brush("#ED8936");
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotImplementedException();

    private static Brush Brush(string color) =>
        new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
}
