using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AnimeTracker.Converters
{
    public class IntToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int count)
            {
                int min = 2;
                if (parameter != null && int.TryParse(parameter.ToString(), out int paramMin))
                    min = paramMin;
                return count >= min ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}