using AnimeTracker.Converters;
using AnimeTracker.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AnimeTracker.Services
{
    /// <summary>
    /// Генерация «красивой» картинки со списком аниме (вкладки «План» и «Топ»).
    ///
    /// Как это работает:
    ///  1. Для каждого аниме строится карточка (WPF-визуал) фиксированной ширины. Высота карточки определяется
    ///     самим текстом (название и жанры переносятся), поэтому ничего не обрезается.
    ///  2. Карточки раскладываются в 2 колонки построчно (в одной строке обе карточки одной высоты).
    ///  3. Строки режутся на страницы: высота одной картинки не превышает <see cref="MaxPageHeight"/> px.
    ///     Если список большой — получится несколько картинок (_1-of-3, _2-of-3, ...), каждая со своей шапкой
    ///     и логотипом; строки между страницами распределяются равномерно (без «хвоста» из 2–3 карточек).
    ///  4. Страницы рендерятся ПО ОДНОЙ и сразу сохраняются — в памяти одновременно только одна большая картинка.
    ///
    /// Цвета берутся из текущей темы приложения, шрифт — из главного окна.
    /// </summary>
    public sealed class ShareImageJob
    {
        private const double PageWidth = 1400;   // уже страница — текст на ней относительно крупнее при просмотре «по размеру экрана»
        private const double OuterMargin = 56;
        private const double ColumnGap = 20;
        private const double RowGap = 16;
        private const double SectionGap = 24;
        private const double MaxPageHeight = 4200; // умеренная высота: удобно листать и не сжимается мессенджерами

        private static readonly IValueConverter GenresConverter = new AnimeGenresConverter();
        private static readonly IValueConverter StatusConverter = new StatusLocalizationConverter();

        private sealed class Row
        {
            public Border Left = null!;
            public Border? Right;
            public double Height;
        }

        private sealed class ThemeBrushes
        {
            public Brush WindowBg = null!;
            public Brush PanelBg = null!;
            public Brush Accent = null!;
            public Brush AccentLight = null!;
            public Brush TextPrimary = null!;
            public Brush TextSecondary = null!;
            public Brush Border = null!;
        }

        private readonly List<List<Row>> _pages = new();
        private readonly bool _isTop;
        private readonly int _totalCount;
        private readonly ThemeBrushes _theme;
        private readonly FontFamily _font;
        private readonly double _cardWidth = (PageWidth - 2 * OuterMargin - ColumnGap) / 2;

        public int PageCount => _pages.Count;

        private ShareImageJob(bool isTop, int totalCount, FontFamily font)
        {
            _isTop = isTop;
            _totalCount = totalCount;
            _font = font;
            _theme = new ThemeBrushes
            {
                WindowBg = Res("WindowBackground", new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x1F))),
                PanelBg = Res("PanelBackground", new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x2E))),
                Accent = Res("AccentBrush", new SolidColorBrush(Color.FromRgb(0x7F, 0x6A, 0xFF))),
                AccentLight = Res("AccentLightBrush", new SolidColorBrush(Color.FromRgb(0x9F, 0x8A, 0xFF))),
                TextPrimary = Res("TextPrimary", Brushes.White),
                TextSecondary = Res("TextSecondary", Brushes.LightGray),
                Border = Res("BorderBrush", new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)))
            };
        }

        /// <summary>Подготавливает раскладку (измеряет карточки и режет на страницы). Вызывать в UI-потоке.</summary>
        public static ShareImageJob Create(IReadOnlyList<Anime> items, bool isTop, FontFamily font)
        {
            var job = new ShareImageJob(isTop, items.Count, font);
            job.Layout(items);
            return job;
        }

        private void Layout(IReadOnlyList<Anime> items)
        {
            // Высота страницы, доступная под строки с карточками
            var sampleHeader = BuildHeader(1, 99);
            sampleHeader.Measure(new Size(PageWidth - 2 * OuterMargin, double.PositiveInfinity));
            var sampleFooter = BuildFooter();
            sampleFooter.Measure(new Size(PageWidth - 2 * OuterMargin, double.PositiveInfinity));
            double budget = MaxPageHeight - 2 * OuterMargin
                            - sampleHeader.DesiredSize.Height - sampleFooter.DesiredSize.Height
                            - 2 * SectionGap;

            var cards = new List<Border>(items.Count);
            for (int i = 0; i < items.Count; i++)
            {
                var card = BuildCard(items[i], i + 1);
                card.Measure(new Size(_cardWidth, double.PositiveInfinity));
                cards.Add(card);
            }

            // Сначала собираем строки (по две карточки в каждой), затем режем их на страницы.
            var rows = new List<Row>();
            for (int i = 0; i < cards.Count; i += 2)
            {
                var left = cards[i];
                var right = i + 1 < cards.Count ? cards[i + 1] : null;
                double h = Math.Max(left.DesiredSize.Height, right?.DesiredSize.Height ?? 0);

                // Обе карточки в строке получают одинаковую высоту — ряд выглядит ровно
                left.Height = h;
                if (right != null) right.Height = h;
                rows.Add(new Row { Left = left, Right = right, Height = h });
            }

            var pages = Paginate(rows, budget);

            // Если страниц несколько — перераспределяем строки поровну, чтобы последняя страница
            // не оказалась почти пустой (например, 46 + 3 карточки). Число страниц при этом не растёт.
            if (pages.Count > 1)
            {
                double total = rows.Sum(r => r.Height + RowGap);
                double maxRow = rows.Max(r => r.Height + RowGap);
                var balanced = Paginate(rows, Math.Min(budget, total / pages.Count + maxRow));
                if (balanced.Count <= pages.Count)
                    pages = balanced;
            }

            _pages.AddRange(pages);
        }

        /// <summary>Жадно раскладывает строки по страницам так, чтобы высота строк на странице не превышала limit.</summary>
        private static List<List<Row>> Paginate(List<Row> rows, double limit)
        {
            var pages = new List<List<Row>>();
            var current = new List<Row>();
            double used = 0;

            foreach (var row in rows)
            {
                if (current.Count > 0 && used + row.Height > limit)
                {
                    pages.Add(current);
                    current = new List<Row>();
                    used = 0;
                }
                current.Add(row);
                used += row.Height + RowGap;
            }

            if (current.Count > 0) pages.Add(current);
            return pages;
        }

        /// <summary>Рендерит одну страницу (UI-поток). Результат заморожен и может сохраняться из другого потока.</summary>
        public BitmapSource RenderPage(int index)
        {
            var rows = _pages[index];

            var body = new Grid { Margin = new Thickness(0, SectionGap, 0, 0) };
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_cardWidth) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ColumnGap) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_cardWidth) });
            for (int r = 0; r < rows.Count; r++)
            {
                body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(rows[r].Height + RowGap) });
                Grid.SetRow(rows[r].Left, r);
                Grid.SetColumn(rows[r].Left, 0);
                body.Children.Add(rows[r].Left);
                if (rows[r].Right != null)
                {
                    Grid.SetRow(rows[r].Right!, r);
                    Grid.SetColumn(rows[r].Right!, 2);
                    body.Children.Add(rows[r].Right);
                }
            }

            var footer = BuildFooter();
            footer.Margin = new Thickness(0, SectionGap, 0, 0);

            var stack = new StackPanel();
            stack.Children.Add(BuildHeader(index + 1, _pages.Count));
            stack.Children.Add(body);
            stack.Children.Add(footer);

            var root = new Border
            {
                Width = PageWidth,
                Background = _theme.WindowBg,
                Padding = new Thickness(OuterMargin),
                Child = stack,
                UseLayoutRounding = true,
                SnapsToDevicePixels = true
            };

            root.Measure(new Size(PageWidth, double.PositiveInfinity));
            double height = Math.Ceiling(root.DesiredSize.Height);
            root.Arrange(new Rect(0, 0, PageWidth, height));

            var bmp = new RenderTargetBitmap((int)PageWidth, (int)height, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(root);
            bmp.Freeze();

            // Отцепляем карточки от визуального дерева, чтобы страницу можно было собрать заново при необходимости
            body.Children.Clear();
            return bmp;
        }

        // ───────────────────────────── Сохранение ─────────────────────────────

        public static string BuildPartPath(string basePath, int index, int count)
        {
            if (count <= 1) return basePath;
            string dir = Path.GetDirectoryName(basePath) ?? string.Empty;
            string name = Path.GetFileNameWithoutExtension(basePath);
            string ext = Path.GetExtension(basePath);
            return Path.Combine(dir, $"{name}_{index + 1}-of-{count}{ext}");
        }

        public static void SaveBitmap(BitmapSource bitmap, string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            BitmapEncoder encoder = ext is ".jpg" or ".jpeg"
                ? new JpegBitmapEncoder { QualityLevel = 92 }
                : new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            encoder.Save(stream);
        }

        // ───────────────────────────── Шапка и подвал ─────────────────────────────

        private FrameworkElement BuildHeader(int page, int pageCount)
        {
            var accent = ColorOf(_theme.Accent, Color.FromRgb(0x7F, 0x6A, 0xFF));
            var gradient = new LinearGradientBrush(
                Color.FromArgb(0x66, accent.R, accent.G, accent.B),
                Color.FromArgb(0x0D, accent.R, accent.G, accent.B), 0);

            var border = new Border
            {
                Background = gradient,
                BorderBrush = _theme.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(22),
                Padding = new Thickness(32, 26, 32, 26)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var logo = CreateLogo();
            logo.Margin = new Thickness(0, 0, 24, 0);
            logo.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(logo, 0);
            grid.Children.Add(logo);

            var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            titles.Children.Add(Text("ANIME TRACKER", 36, _theme.TextPrimary, FontWeights.Bold, wrap: false));
            titles.Children.Add(Text(Loc(_isTop ? "Share_TopTitle" : "Share_PlanTitle",
                                         _isTop ? "Мой топ аниме" : "Мой план просмотра"),
                                     22, _theme.AccentLight, FontWeights.SemiBold, wrap: false, margin: new Thickness(0, 4, 0, 0)));
            Grid.SetColumn(titles, 1);
            grid.Children.Add(titles);

            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            info.Children.Add(Text(string.Format(Loc("Share_Total", "Всего: {0}"), _totalCount),
                                   24, _theme.TextPrimary, FontWeights.Bold, TextAlignment.Right, wrap: false));
            info.Children.Add(Text(DateTime.Now.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
                                   15, _theme.TextSecondary, FontWeights.SemiBold, TextAlignment.Right, wrap: false, margin: new Thickness(0, 4, 0, 0)));
            if (pageCount > 1)
                info.Children.Add(Text(string.Format(Loc("Share_Page", "Стр. {0} из {1}"), page, pageCount),
                                       15, _theme.AccentLight, FontWeights.SemiBold, TextAlignment.Right, wrap: false, margin: new Thickness(0, 4, 0, 0)));
            Grid.SetColumn(info, 2);
            grid.Children.Add(info);

            border.Child = grid;
            return border;
        }

        private FrameworkElement BuildFooter()
        {
            return Text("✦ " + Loc("Share_Footer", "Создано в Anime Tracker"),
                        15, _theme.TextSecondary, FontWeights.SemiBold, TextAlignment.Center, wrap: false);
        }

        private FrameworkElement CreateLogo()
        {
            var icon = LogoIcon.Value;
            if (icon != null)
            {
                var image = new Image { Source = icon, Width = 76, Height = 76, Stretch = Stretch.Uniform };
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
                return image;
            }
            return Text("✦", 60, _theme.Accent, FontWeights.Bold, TextAlignment.Center, wrap: false);
        }

        private static readonly Lazy<BitmapSource?> LogoIcon = new(() =>
        {
            try
            {
                var decoder = BitmapDecoder.Create(new Uri("pack://application:,,,/Resources/app.ico"),
                    BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                var frame = decoder.Frames.OrderByDescending(f => f.PixelWidth).First();
                frame.Freeze();
                return frame;
            }
            catch
            {
                return null; // иконки нет — будет текстовый логотип ✦
            }
        });

        // ───────────────────────────── Карточка аниме ─────────────────────────────

        private Border BuildCard(Anime a, int number)
        {
            string rankText = number.ToString(CultureInfo.InvariantCulture);
            Brush badgeBg = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
            Brush badgeFg = _theme.TextPrimary;

            if (_isTop)
            {
                rankText = Convert.ToString(a.Rank, CultureInfo.InvariantCulture) ?? rankText;
                if (int.TryParse(rankText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int r))
                {
                    Color? medal = r switch
                    {
                        1 => Color.FromRgb(0xFF, 0xD7, 0x00),
                        2 => Color.FromRgb(0xC0, 0xC0, 0xC0),
                        3 => Color.FromRgb(0xCD, 0x7F, 0x32),
                        _ => null
                    };
                    if (medal.HasValue)
                    {
                        badgeBg = new SolidColorBrush(medal.Value);
                        badgeFg = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20));
                    }
                }
            }

            var card = new Border
            {
                Width = _cardWidth,
                Background = _theme.PanelBg,
                BorderBrush = _theme.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(16),
                VerticalAlignment = VerticalAlignment.Top
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Номер / место в топе
            var badge = new Border
            {
                Width = 60,
                Height = 60,
                CornerRadius = new CornerRadius(14),
                Background = badgeBg,
                VerticalAlignment = VerticalAlignment.Center,
                Child = Text(rankText, rankText.Length > 3 ? 16 : 22, badgeFg, FontWeights.Bold, TextAlignment.Center, wrap: false,
                             vAlign: VerticalAlignment.Center)
            };
            Grid.SetColumn(badge, 0);
            grid.Children.Add(badge);

            // Название, жанры, мета-информация
            var info = new StackPanel { Margin = new Thickness(16, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(Text(a.Title ?? string.Empty, 22, _theme.TextPrimary, FontWeights.Bold));

            string genres = GetGenres(a);
            if (!string.IsNullOrWhiteSpace(genres))
                info.Children.Add(Text(genres, 15, _theme.TextSecondary, FontWeights.SemiBold, margin: new Thickness(0, 5, 0, 0)));

            string meta = GetMeta(a);
            if (!string.IsNullOrWhiteSpace(meta))
                info.Children.Add(Text(meta, 15, _theme.AccentLight, FontWeights.SemiBold, margin: new Thickness(0, 7, 0, 0)));

            Grid.SetColumn(info, 1);
            grid.Children.Add(info);

            // Статус (план) или балл (топ)
            Border pill;
            if (_isTop)
            {
                pill = MakePill(a.AdjustedScore.ToString("F2", CultureInfo.InvariantCulture), _theme.Accent, Brushes.White, 22);
            }
            else
            {
                string status = GetStatus(a);
                var (bg, fg) = StatusColors(Convert.ToString(a.Status, CultureInfo.InvariantCulture) ?? string.Empty);
                pill = MakePill(status, bg, fg, 17);
            }
            Grid.SetColumn(pill, 2);
            grid.Children.Add(pill);

            card.Child = grid;
            return card;
        }

        private Border MakePill(string text, Brush bg, Brush fg, double fontSize)
        {
            return new Border
            {
                MinWidth = 108,
                Background = bg,
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(14, 8, 14, 8),
                VerticalAlignment = VerticalAlignment.Center,
                Child = Text(text, fontSize, fg, FontWeights.Bold, TextAlignment.Center, wrap: false)
            };
        }

        private static (Brush bg, Brush fg) StatusColors(string status)
        {
            switch (status)
            {
                case "План":
                case "Plan":
                    return (new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B)), Brushes.White);
                case "Смотрю":
                case "Watching":
                    return (new SolidColorBrush(Color.FromRgb(0xFF, 0xD9, 0x3D)), Brushes.Black);
                case "Просмотрено":
                case "Watched":
                    return (new SolidColorBrush(Color.FromRgb(0x6B, 0xCB, 0x77)), Brushes.White);
                default:
                    return (new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), Brushes.White);
            }
        }

        private static string GetGenres(Anime a)
        {
            try
            {
                if (GenresConverter.Convert(a, typeof(string), null!, CultureInfo.CurrentUICulture) is string s
                    && !string.IsNullOrWhiteSpace(s))
                    return s;
            }
            catch { /* берём запасной вариант */ }
            return a.Genre ?? string.Empty;
        }

        private static string GetStatus(Anime a)
        {
            string raw = Convert.ToString(a.Status, CultureInfo.InvariantCulture) ?? string.Empty;
            try
            {
                if (StatusConverter.Convert(a.Status, typeof(string), null!, CultureInfo.CurrentUICulture) is string s
                    && !string.IsNullOrWhiteSpace(s))
                    return s;
            }
            catch { /* берём запасной вариант */ }
            return raw;
        }

        private static string GetMeta(Anime a)
        {
            var parts = new List<string>();

            string years = Convert.ToString(a.YearsDisplay, CultureInfo.InvariantCulture) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(years)) parts.Add("🗓 " + years);

            int episodes = a.Seasons.Sum(s => s.EpisodeCount);
            if (episodes > 0) parts.Add($"📺 {episodes} {Loc("Share_EpisodesShort", "эп.")}");

            int movies = a.Movies.Count;
            if (movies > 0) parts.Add($"🎞 {movies}");

            string voice = a.VoiceOver ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(voice)) parts.Add("🎙 " + voice);

            return string.Join("  ·  ", parts);
        }

        // ───────────────────────────── Вспомогательные методы ─────────────────────────────

        private TextBlock Text(string text, double size, Brush foreground, FontWeight weight,
                               TextAlignment align = TextAlignment.Left, bool wrap = true,
                               Thickness? margin = null, VerticalAlignment vAlign = VerticalAlignment.Top)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = size,
                Foreground = foreground,
                FontWeight = weight,
                FontFamily = _font,
                TextAlignment = align,
                TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
                Margin = margin ?? new Thickness(0),
                VerticalAlignment = vAlign,
                HorizontalAlignment = align == TextAlignment.Center ? HorizontalAlignment.Center : HorizontalAlignment.Stretch
            };
        }

        private static Brush Res(string key, Brush fallback)
            => Application.Current?.TryFindResource(key) as Brush ?? fallback;

        private static Color ColorOf(Brush brush, Color fallback)
            => (brush as SolidColorBrush)?.Color ?? fallback;

        private static string Loc(string key, string fallback)
            => Application.Current?.TryFindResource(key) as string ?? fallback;
    }

    /// <summary>Тонкая обёртка, чтобы вызывать генерацию как ShareImageService.Create(...).</summary>
    public static class ShareImageService
    {
        public static ShareImageJob Create(IReadOnlyList<Anime> items, bool isTop, FontFamily font)
            => ShareImageJob.Create(items, isTop, font);

        public static string BuildPartPath(string basePath, int index, int count)
            => ShareImageJob.BuildPartPath(basePath, index, count);

        public static void SaveBitmap(BitmapSource bitmap, string path)
            => ShareImageJob.SaveBitmap(bitmap, path);
    }
}
