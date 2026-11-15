using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace AnimeTracker.Converters
{
    /// <summary>
    /// Цвета плашки статуса аниме в таблице «План»: «План» — красный, «Смотрю» — жёлтый, «Просмотрено» — зелёный.
    /// Понимает статусы на обоих языках (русском и английском).
    /// <para>
    /// Заменяет двенадцать DataTrigger на каждую строку таблицы: конвертер работает быстрее, потому что
    /// при создании строки не нужно создавать и подписывать множество триггеров — а это заметно при прокрутке.
    /// </para>
    /// </summary>
    public class StatusBackgroundConverter : IValueConverter
    {
        private static readonly Brush PlanBrush = Create(0xFF, 0x6B, 0x6B);
        private static readonly Brush WatchingBrush = Create(0xFF, 0xD9, 0x3D);
        private static readonly Brush WatchedBrush = Create(0x6B, 0xCB, 0x77);

        private static Brush Create(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze(); // замороженная кисть дешевле при отрисовке и безопасно разделяется между строками
            return brush;
        }

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return (value as string) switch
            {
                "План" or "Plan" => PlanBrush,
                "Смотрю" or "Watching" => WatchingBrush,
                "Просмотрено" or "Watched" => WatchedBrush,
                _ => Brushes.Transparent
            };
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Цвет текста на плашке статуса: на жёлтом («Смотрю») — чёрный, на остальных — белый.
    /// </summary>
    public class StatusForegroundConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return (value as string) is "Смотрю" or "Watching" ? Brushes.Black : Brushes.White;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}