using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Windows;

namespace AnimeTracker.Services
{
    /// <summary>
    /// Менеджер локализации: переключает язык интерфейса (Русский / English), подменяя словарь строк
    /// в ресурсах приложения, и сохраняет выбранный язык в файл настроек.
    /// Остальные части приложения узнают о смене языка через событие <see cref="LanguageChanged"/>.
    /// </summary>
    public static class LocalizationManager
    {
        private const string EnglishName = "English";
        private const string RussianName = "Русский";

        // Подстрока, по которой словарь строк опознаётся среди MergedDictionaries
        // (оба словаря называются Strings.*.xaml, словари тем так не называются).
        private const string StringsDictionaryMarker = "Strings";

        private static string? _settingsFile;

        // Загруженные словари строк. Разбор XAML — дорогая операция, поэтому каждый язык
        // загружается один раз, а при повторных переключениях словарь берётся из кэша.
        private static readonly Dictionary<string, ResourceDictionary> DictionaryCache = new();

        /// <summary>Вызывается после того, как словарь строк заменён и новый язык применён.</summary>
        public static event Action? LanguageChanged;

        /// <summary>Текущий язык: «Русский» или «English».</summary>
        public static string CurrentLanguage { get; private set; } = RussianName;

        /// <summary>
        /// Инициализация: читает язык из файла настроек и применяет его.
        /// Путь к файлу настроек передаётся снаружи, чтобы менеджер не зависел от расположения данных приложения.
        /// </summary>
        public static void Initialize(string settingsFile)
        {
            _settingsFile = settingsFile;
            string lang = LoadLanguage();
            SetLanguage(lang);
        }

        private static string LoadLanguage()
        {
            if (_settingsFile == null || !File.Exists(_settingsFile)) return RussianName;
            try
            {
                string json = File.ReadAllText(_settingsFile);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                return settings?.Language ?? RussianName;
            }
            catch { return RussianName; }
        }

        /// <summary>
        /// Применяет язык: подменяет словарь строк, обновляет культуру потока, сохраняет выбор
        /// и уведомляет подписчиков.
        /// </summary>
        public static void SetLanguage(string language)
        {
            CurrentLanguage = language;
            bool isEnglish = language == EnglishName;

            ApplyStringsDictionary(isEnglish);

            var culture = new CultureInfo(isEnglish ? "en-US" : "ru-RU");
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;

            SaveLanguage(language);
            LanguageChanged?.Invoke();
        }

        /// <summary>Заменяет в ресурсах приложения словарь строк на словарь выбранного языка.</summary>
        private static void ApplyStringsDictionary(bool isEnglish)
        {
            string source = isEnglish ? "Resources/Strings.en.xaml" : "Resources/Strings.ru.xaml";

            if (!DictionaryCache.TryGetValue(source, out var dict))
            {
                dict = new ResourceDictionary { Source = new Uri(source, UriKind.Relative) };
                DictionaryCache[source] = dict;
            }

            var merged = Application.Current.Resources.MergedDictionaries;

            // Если нужный словарь уже подключён — ничего не меняем (лишняя замена вызвала бы перерисовку всех DynamicResource)
            if (merged.Contains(dict)) return;

            var oldDict = merged.FirstOrDefault(d => d.Source != null
                                                     && d.Source.OriginalString.Contains(StringsDictionaryMarker));
            if (oldDict != null)
                merged.Remove(oldDict);

            merged.Add(dict);
        }

        private static void SaveLanguage(string language)
        {
            if (_settingsFile == null) return;
            try
            {
                string json = File.Exists(_settingsFile) ? File.ReadAllText(_settingsFile) : "{}";
                var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();

                // Язык не изменился (например, при запуске приложения) — файл перезаписывать не нужно
                if (File.Exists(_settingsFile) && settings.Language == language) return;

                settings.Language = language;
                File.WriteAllText(_settingsFile, JsonSerializer.Serialize(settings));
            }
            catch { /* настройки — не критичны: при сбое просто останется прежний язык по умолчанию */ }
        }

        /// <summary>Часть файла настроек, за которую отвечает менеджер локализации.</summary>
        private class AppSettings
        {
            public string Theme { get; set; } = "По умолчанию";
            public string Language { get; set; } = RussianName;
        }
    }
}
