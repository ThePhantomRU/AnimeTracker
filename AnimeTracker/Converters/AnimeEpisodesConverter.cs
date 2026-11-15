using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;
using AnimeTracker.Models;
using AnimeTracker.Services;

namespace AnimeTracker.Converters
{
    /// <summary>
    /// Формирует многострочное описание состава аниме для таблицы:
    /// <code>
    /// 🎬 1 сезон (25 эп.)
    /// 🎬 2 сезон (12 эп.)
    /// 🎥 2 фильма
    /// 📀 1 OVA
    /// 🌐 1 ONA
    /// </code>
    /// Сезоны выводятся по одному (с номером и числом серий), фильмы, OVA и ONA — общим количеством.
    /// Если частей нет — возвращает «—».
    /// </summary>
    public class AnimeEpisodesConverter : IValueConverter
    {
        private const string NoValue = "—";

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not Anime anime)
                return NoValue;

            bool isEnglish = LocalizationManager.CurrentLanguage == "English";
            var lines = new List<string>(anime.Seasons.Count + 3);

            // Сезоны: каждый отдельной строкой. Слово «сезон» не склоняется ("1 сезон", "2 сезон"...)
            string seasonLabel = isEnglish ? "season" : "сезон";
            string episodesLabel = isEnglish ? "ep." : "эп.";
            for (int i = 0; i < anime.Seasons.Count; i++)
                lines.Add($"🎬 {i + 1} {seasonLabel} ({anime.Seasons[i].EpisodeCount} {episodesLabel})");

            // Фильмы: общее количество с правильным склонением
            if (anime.Movies.Count > 0)
                lines.Add($"🎥 {anime.Movies.Count} {GetMoviesLabel(anime.Movies.Count, isEnglish)}");

            // OVA: общее количество, аббревиатура не склоняется
            if (anime.Ovas.Count > 0)
                lines.Add($"📀 {anime.Ovas.Count} OVA");

            // ONA (аниме, вышедшее в интернете): общее количество, аббревиатура не склоняется
            if (anime.Onas.Count > 0)
                lines.Add($"🌐 {anime.Onas.Count} ONA");

            return lines.Count > 0 ? string.Join("\n", lines) : NoValue;
        }

        /// <summary>
        /// Слово «фильм» в нужной форме. Русский язык: 1 фильм, 2–4 фильма, 5+ фильмов
        /// (здесь используется упрощённое правило, как и раньше: без учёта чисел 11–14, 21 и т. п.).
        /// </summary>
        private static string GetMoviesLabel(int count, bool isEnglish)
        {
            if (isEnglish)
                return count == 1 ? "movie" : "movies";

            if (count == 1) return "фильм";
            if (count >= 2 && count <= 4) return "фильма";
            return "фильмов";
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}