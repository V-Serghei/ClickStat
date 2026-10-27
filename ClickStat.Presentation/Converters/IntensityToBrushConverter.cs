using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ClickStat.Presentation.Converters
{
    public class IntensityToBrushConverter : IMultiValueConverter
    {
        private static readonly SolidColorBrush Level0 = new(Color.FromRgb(0x26, 0x26, 0x38));
        private static readonly SolidColorBrush Level1 = new(Color.FromRgb(0x42, 0x99, 0xE1));
        private static readonly SolidColorBrush Level2 = new(Color.FromRgb(0x48, 0xBB, 0x78));
        private static readonly SolidColorBrush Level3 = new(Color.FromRgb(0xE9, 0xD5, 0x2D));
        private static readonly SolidColorBrush Level4 = new(Color.FromRgb(0xED, 0x89, 0x36));
        private static readonly SolidColorBrush Level5 = new(Color.FromRgb(0xF5, 0x65, 0x65));

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 3 
                || values[0] is not Dictionary<string, int> keyCounts 
                || values[1] is not string keyName 
                || values[2] is not int maxCount)
            {
                return Level0;
            }

            if (!keyCounts.TryGetValue(keyName, out int count) || maxCount == 0 || count == 0)
            {
                return Level0;
            }

            double intensity = (double)count / maxCount;

            if (intensity > 0.8) return Level5;
            if (intensity > 0.6) return Level4;
            if (intensity > 0.4) return Level3;
            if (intensity > 0.2) return Level2;
            
            return Level1;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}