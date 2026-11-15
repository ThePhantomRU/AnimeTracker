using System;
using System.Globalization;
using System.Windows.Data;

namespace AnimeTracker.Converters
{
    public class IntZeroToEmptyConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int intValue)
                return intValue == 0 ? string.Empty : intValue.ToString();
            if (value is double doubleValue && targetType == typeof(string))
                return doubleValue == 0 ? string.Empty : doubleValue.ToString();
            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string str && string.IsNullOrWhiteSpace(str))
                return 0;
            if (value is string str2 && int.TryParse(str2, out int result))
                return result;
            return 0;
        }
    }
}