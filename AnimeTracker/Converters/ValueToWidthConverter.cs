using System;
using System.Globalization;
using System.Windows.Data;

namespace AnimeTracker.Converters
{
    public class ValueToWidthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double percent)
                return Math.Max(0, Math.Min(100, percent)); // ограничим 0-100%
            return 0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}