using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;
using AnimeTracker.Services;
using AnimeTracker.ViewModels;

namespace AnimeTracker.Converters
{
    public class ScoreDescriptionConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double doubleValue && parameter is string criterion)
            {
                int score = (int)Math.Round(doubleValue);
                bool isEnglish = LocalizationManager.CurrentLanguage == "English";
                var dict = isEnglish ? MainViewModel.ScoreDescriptionsEn : MainViewModel.ScoreDescriptionsRu;

                if (dict.TryGetValue(criterion, out var critDict) && critDict.TryGetValue(score, out var description))
                    return description;
            }
            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}