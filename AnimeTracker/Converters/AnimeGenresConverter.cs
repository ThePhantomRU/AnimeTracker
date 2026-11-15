using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;
using AnimeTracker.Models;
using AnimeTracker.Services;
using AnimeTracker.ViewModels;

namespace AnimeTracker.Converters
{
    /// <summary>
    /// Собирает жанры аниме (основной + дополнительные) в одну строку через запятую
    /// на текущем языке интерфейса. Если жанров нет — возвращает «—».
    /// </summary>
    public class AnimeGenresConverter : IValueConverter
    {
        private const string NoValue = "—";

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not Anime anime)
                return NoValue;

            bool isEnglish = LocalizationManager.CurrentLanguage == "English";
            var genres = new List<string>(4);

            // Основной жанр
            if (!string.IsNullOrWhiteSpace(anime.Genre))
                genres.Add(Localize(anime.Genre, isEnglish));

            // Дополнительные жанры (пустые значения пропускаются)
            if (anime.AdditionalGenres != null)
            {
                foreach (var genre in anime.AdditionalGenres)
                {
                    if (!string.IsNullOrWhiteSpace(genre))
                        genres.Add(Localize(genre, isEnglish));
                }
            }

            return genres.Count > 0 ? string.Join(", ", genres) : NoValue;
        }

        // В данных жанры хранятся на русском; английское название нужно только для отображения.
        private static string Localize(string genre, bool isEnglish)
            => isEnglish ? MainViewModel.GetEnglishGenre(genre) : genre;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}