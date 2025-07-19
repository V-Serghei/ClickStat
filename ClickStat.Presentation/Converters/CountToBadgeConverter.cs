using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ClickStat.Presentation.Converters
{
    
    public class CountToBadgeConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            
            if (values.Length < 2
                || values[0] is not Dictionary<string, int> keyCounts
                || values[1] is not string keyName)
            {
                
                return targetType == typeof(Visibility) ? Visibility.Collapsed : null;
            }

            if (!keyCounts.TryGetValue(keyName, out int count) || count == 0)
            {
                return targetType == typeof(Visibility) ? Visibility.Collapsed : null;
            }

            
            if (parameter is string param && param == "visibility")
            {
                return Visibility.Visible;
            }

            
            if (parameter is StringFormat)
            {
                return count;
            }

            if (count > 999)
            {
                return $"{count / 1000.0:0.#}k";
            }
                
            return count.ToString();
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}