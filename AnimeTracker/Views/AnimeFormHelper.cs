using AnimeTracker.Models;
using AnimeTracker.Services;
using AnimeTracker.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AnimeTracker.Views
{
    /// <summary>
    /// Общий код окон «Добавить аниме», «Редактировать аниме» и «Предварительная оценка».
    /// Раньше эти методы дублировались в каждом окне; теперь они собраны в одном месте,
    /// поэтому правка (например, правила проверки формы) делается один раз.
    /// </summary>
    internal static class AnimeFormHelper
    {
        // ───────────── Локализованные списки жанров и статусов ─────────────
        //
        // В окнах выпадающие списки привязаны к «каноническим» (русским) ключам жанров и статусов,
        // а на экране показывается переведённый текст (Display). Благодаря этому смена языка
        // не сбрасывает выбор пользователя.

        private static bool IsEnglish => LocalizationManager.CurrentLanguage == "English";

        /// <summary>Список жанров для текущего языка, отсортированный по отображаемому названию.</summary>
        public static List<LocalizedOption> BuildGenreOptions()
        {
            bool isEnglish = IsEnglish;
            return MainViewModel.GenreOptions
                .Select(key => new LocalizedOption
                {
                    Key = key,
                    Display = string.IsNullOrEmpty(key) ? "" : (isEnglish ? MainViewModel.GetEnglishGenre(key) : key)
                })
                .OrderBy(o => o.Display)
                .ToList();
        }

        /// <summary>Список статусов («План», «Смотрю», «Просмотрено») для текущего языка.</summary>
        public static List<LocalizedOption> BuildStatusOptions()
        {
            bool isEnglish = IsEnglish;
            var keys = new[] { "План", "Смотрю", "Просмотрено" };
            return keys
                .Select(key => new LocalizedOption
                {
                    Key = key,
                    Display = isEnglish ? MainViewModel.GetEnglishStatus(key) : key
                })
                .ToList();
        }

        /// <summary>
        /// Пересчитывает варианты для каждого поля «дополнительный жанр»: жанр, уже выбранный
        /// в основном поле или в других дополнительных, из списка исключается. Собственный выбор поля
        /// всегда остаётся в его списке.
        /// <para>
        /// Список жанров строится и сортируется один раз, а не заново для каждого поля
        /// (порядок результата тот же: фильтрация не нарушает порядок уже отсортированного списка).
        /// </para>
        /// </summary>
        public static void UpdateAdditionalGenreItemsSources(string? genreKey, IEnumerable<AdditionalGenreItem> items)
        {
            var itemList = items as IList<AdditionalGenreItem> ?? items.ToList();
            if (itemList.Count == 0) return;

            var allOptions = BuildGenreOptions();

            var selectedKeys = new HashSet<string>();
            if (!string.IsNullOrEmpty(genreKey))
                selectedKeys.Add(genreKey);
            foreach (var item in itemList)
                if (!string.IsNullOrEmpty(item.SelectedGenreKey))
                    selectedKeys.Add(item.SelectedGenreKey);

            foreach (var item in itemList)
            {
                item.AvailableGenreOptions = allOptions
                    .Where(option => !selectedKeys.Contains(option.Key) || option.Key == item.SelectedGenreKey)
                    .ToList();
            }
        }

        /// <summary>Переводит канонический (русский) жанр в вид, в котором он сохраняется: на языке интерфейса.</summary>
        public static string ToStoredGenre(string key) => IsEnglish ? MainViewModel.GetEnglishGenre(key) : key;

        /// <summary>Переводит канонический (русский) статус в вид, в котором он сохраняется: на языке интерфейса.</summary>
        public static string ToStoredStatus(string key) => IsEnglish ? MainViewModel.GetEnglishStatus(key) : key;

        // ───────────── Проверка формы ─────────────

        /// <summary>
        /// Проверяет заполнение формы аниме и показывает предупреждение при первой найденной проблеме:
        /// 1) обязательные поля; 2) хотя бы один сезон/фильм/OVA/ONA; 3) корректные годы и число серий.
        /// </summary>
        public static bool ValidateForm(
            string? title, string? genreKey, string? description, string? voiceOver,
            int seasonsCount, int moviesCount, int ovasCount, int onasCount,
            IEnumerable<Season> seasons, IEnumerable<Movie> movies, IEnumerable<Ova> ovas, IEnumerable<Ona> onas)
        {
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(genreKey) ||
                string.IsNullOrWhiteSpace(description) || string.IsNullOrWhiteSpace(voiceOver))
            {
                ShowWarning("Msg_RequiredFields");
                return false;
            }

            if (seasonsCount == 0 && moviesCount == 0 && ovasCount == 0 && onasCount == 0)
            {
                ShowWarning("Msg_AddAtLeastOne");
                return false;
            }

            if (seasons.Any(s => s.Year <= 0 || s.EpisodeCount <= 0) ||
                movies.Any(m => m.Year <= 0) ||
                ovas.Any(o => o.Year <= 0 || o.EpisodeCount <= 0) ||
                onas.Any(o => o.Year <= 0 || o.EpisodeCount <= 0))
            {
                ShowWarning("Msg_CorrectYears");
                return false;
            }

            return true;
        }

        private static void ShowWarning(string messageKey)
        {
            MessageBox.Show(MainViewModel.GetLocalizedString(messageKey),
                            MainViewModel.GetLocalizedString("Msg_ErrorTitle"),
                            MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        // ───────────── Работа с частями (сезоны, фильмы, OVA, ONA) ─────────────

        /// <summary>Приводит список к нужному размеру: недостающие элементы создаются, лишние удаляются с конца.</summary>
        public static void ResizeList<T>(IList<T> list, int size, Func<T> create)
        {
            while (list.Count < size) list.Add(create());
            while (list.Count > size) list.RemoveAt(list.Count - 1);
        }

        /// <summary>
        /// Синхронизирует список частей оригинального аниме с редактируемой копией:
        /// выравнивает размер и копирует значения. Существующие объекты частей сохраняются
        /// (вместе со всеми их оценками) — заменяются только данные, которые правит форма.
        /// </summary>
        public static void SyncParts<T>(IList<T> target, IList<T> source, Func<T> create, Action<T, T> copyValues)
        {
            ResizeList(target, source.Count, create);
            for (int i = 0; i < source.Count; i++)
                copyValues(target[i], source[i]);
        }

        // ───────────── Копирование оценок между основными и временными ─────────────

        /// <summary>Временные оценки части → основные.</summary>
        public static void CopyTempScoresToMain(PartBase part)
        {
            part.Story = part.TempStory;
            part.MainCharacters = part.TempMainCharacters;
            part.Characters = part.TempCharacters;
            part.Visual = part.TempVisual;
            part.Sound = part.TempSound;
            part.Emotions = part.TempEmotions;
            part.Originality = part.TempOriginality;
            part.Finale = part.TempFinale;
            part.OpeningScores = new ObservableCollection<double>(part.TempOpeningScores);
            part.EndingScores = new ObservableCollection<double>(part.TempEndingScores);
        }

        /// <summary>Основные оценки части → временные.</summary>
        public static void CopyMainScoresToTemp(PartBase part)
        {
            part.TempStory = part.Story;
            part.TempMainCharacters = part.MainCharacters;
            part.TempCharacters = part.Characters;
            part.TempVisual = part.Visual;
            part.TempSound = part.Sound;
            part.TempEmotions = part.Emotions;
            part.TempOriginality = part.Originality;
            part.TempFinale = part.Finale;
            part.TempOpeningScores = new ObservableCollection<double>(part.OpeningScores);
            part.TempEndingScores = new ObservableCollection<double>(part.EndingScores);
        }

        // ───────────── Элементы интерфейса ─────────────

        /// <summary>Рекурсивно перечисляет все визуальные элементы заданного типа внутри родителя.</summary>
        public static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t) yield return t;
                foreach (var nested in FindVisualChildren<T>(child))
                    yield return nested;
            }
        }

        /// <summary>Подключает защиту от вставки нечисловых значений ко всем TextBox внутри окна (на момент вызова).</summary>
        public static void AttachPasteGuards(DependencyObject root)
        {
            foreach (var textBox in FindVisualChildren<TextBox>(root))
                DataObject.AddPastingHandler(textBox, OnPasting);
        }

        /// <summary>Снимает обработчики, подключённые в <see cref="AttachPasteGuards"/>.</summary>
        public static void DetachPasteGuards(DependencyObject root)
        {
            foreach (var textBox in FindVisualChildren<TextBox>(root))
                DataObject.RemovePastingHandler(textBox, OnPasting);
        }

        /// <summary>Запрещает вставку текста, который не является целым числом.</summary>
        private static void OnPasting(object sender, DataObjectPastingEventArgs e)
        {
            if (e.SourceDataObject.GetDataPresent(DataFormats.Text, true))
            {
                string? pastedText = e.SourceDataObject.GetData(DataFormats.Text) as string;
                if (!string.IsNullOrEmpty(pastedText) && !int.TryParse(pastedText, out _))
                    e.CancelCommand();
            }
        }

        /// <summary>Прокрутка колесом мыши внутри многострочного TextBox (его собственный ScrollViewer).</summary>
        public static void ScrollTextBoxWithWheel(object sender, MouseWheelEventArgs e)
        {
            e.Handled = true;
            if (sender is TextBox textBox && textBox.Template.FindName("PART_ContentHost", textBox) is ScrollViewer sv)
                sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta / 3);
        }
    }
}