using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;
using AnimeTracker.Services;

namespace AnimeTracker.Converters
{
    /// <summary>
    /// Преобразует статус аниме в текст на текущем языке интерфейса.
    /// <para>
    /// В данных статус может храниться на любом из двух языков (в зависимости от того, какой язык был
    /// активен при сохранении): «План» / «Смотрю» / «Просмотрено» либо «Plan» / «Watching» / «Watched».
    /// Конвертер показывает его на языке интерфейса; неизвестные значения возвращает без изменений.
    /// </para>
    /// </summary>
    public class StatusLocalizationConverter : IValueConverter
    {
        // Русские (канонические) названия → английские. Таблицы создаются один раз на всё приложение.
        private static readonly Dictionary<string, string> RuToEn = new()
        {
            ["План"] = "Plan",
            ["Смотрю"] = "Watching",
            ["Просмотрено"] = "Watched"
        };

        // Обратное соответствие: английские названия → русские (для ConvertBack).
        private static readonly Dictionary<string, string> EnToRu = new()
        {
            ["Plan"] = "План",
            ["Watching"] = "Смотрю",
            ["Watched"] = "Просмотрено"
        };

        /// <summary>Модель → интерфейс: на английском языке подставляет английское название статуса.</summary>
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string status)
            {
                bool isEnglish = LocalizationManager.CurrentLanguage == "English";
                // В русском режиме статус отображается как есть (ровно как и раньше)
                return isEnglish && RuToEn.TryGetValue(status, out var english) ? english : status;
            }
            return value;
        }

        /// <summary>Интерфейс → модель: приводит английское название к русскому ключу.</summary>
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string status)
                return EnToRu.TryGetValue(status, out var russian) ? russian : status;
            return value;
        }
    }
}