using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ClickStat.Presentation.Converters;

/// <summary>
/// values[0] = HashSet&lt;string&gt; FlashingKeys
/// values[1] = string keyName (Button.Content)
/// Returns Visibility.Visible when the key is actively being pressed, Collapsed otherwise.
/// </summary>
public class KeyFlashConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 2
            && values[0] is HashSet<string> flashing
            && values[1] is string keyName
            && flashing.Contains(keyName))
            return Visibility.Visible;

        return Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
