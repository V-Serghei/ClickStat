using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ClickStat.Presentation.Converters
{
    public class CountToColorConverter : IMultiValueConverter
    {
        public Color BaseColor { get; set; } = Color.FromRgb(34, 35, 51);
        public Color HighlightColor { get; set; } = Color.FromRgb(170, 112, 255);

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            
            if (values == null || values.Length < 2 || values[0] == null || values[1] == null)
                return new SolidColorBrush(BaseColor);

            
            if (!int.TryParse(values[0].ToString(), out int count) || 
                !int.TryParse(values[1].ToString(), out int maxCount) || 
                maxCount == 0)
            {
                return new SolidColorBrush(BaseColor);
            }

            
            double intensity = (double)count / maxCount;
            
            
            if (intensity > 0.8)
                return new SolidColorBrush(Color.FromRgb(255, 85, 85));
            
            if (intensity > 0.6)
                return new SolidColorBrush(Color.FromRgb(255, 153, 85));
            
            if (intensity > 0.4)
                return new SolidColorBrush(Color.FromRgb(255, 221, 85));
            
            if (intensity > 0.2)
                return new SolidColorBrush(Color.FromRgb(102, 221, 85));
            
            if (intensity > 0)
                return new SolidColorBrush(Color.FromRgb(85, 153, 255));
            
            
            return new SolidColorBrush(BaseColor);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}