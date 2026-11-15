using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace AnimeTracker.Models
{
    /// <summary>Обёртка над одной оценкой (1–10) с уведомлением об изменении — для привязки в списках.</summary>
    public class ScoreItem : INotifyPropertyChanged
    {
        private double _value = 1;
        public double Value { get => _value; set { _value = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>
    /// Аниме-тайтл: основные данные (название, жанры, статус, описание, озвучка), его части
    /// (сезоны, фильмы, OVA, ONA) и расчётные оценки. Оцениваются сезоны и фильмы; OVA и ONA учитываются только в составе и статистике.
    /// <para>
    /// Итоговые оценки по критериям — это среднее по всем частям тайтла. Расчёты выполняются
    /// напрямую через делегаты (без рефлексии), поэтому свойство <see cref="Score"/> безопасно
    /// вызывать в цикле, при сортировке и при сериализации.
    /// </para>
    /// <para>ВАЖНО: набор публичных свойств определяет формат JSON-файла данных — не переименовывайте их.</para>
    /// </summary>
    public class Anime : INotifyPropertyChanged
    {
        private string title = "";
        private string genre = "";
        private List<string> additionalGenres = new();
        private string status = "План";
        private string description = "";
        private string voiceOver = "";
        private bool _isEpisodesExpanded = false;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        public void RaisePropertyChanged(string propertyName) => OnPropertyChanged(propertyName);

        public string Title { get => title; set { title = value; OnPropertyChanged(); } }
        public string Genre { get => genre; set { genre = value; OnPropertyChanged(); } }
        public List<string> AdditionalGenres { get => additionalGenres; set { additionalGenres = value; OnPropertyChanged(); } }
        public string Status { get => status; set { status = value; OnPropertyChanged(); } }
        public string Description { get => description; set { description = value; OnPropertyChanged(); } }
        public string VoiceOver { get => voiceOver; set { voiceOver = value; OnPropertyChanged(); } }

        // Новое свойство для управления разворачиванием эпизодов
        public bool IsEpisodesExpanded
        {
            get => _isEpisodesExpanded;
            set { _isEpisodesExpanded = value; OnPropertyChanged(); }
        }

        [JsonIgnore]
        public int StatusPriority => (Status == "Смотрю" || Status == "Watching") ? 0 : 1;

        // ───────────── Итоговые оценки по критериям (среднее по всем частям) ─────────────
        public double Story => AverageOf(GetRatedParts(), p => p.Story);
        public double MainCharacters => AverageOf(GetRatedParts(), p => p.MainCharacters);
        public double Characters => AverageOf(GetRatedParts(), p => p.Characters);
        public double Visual => AverageOf(GetRatedParts(), p => p.Visual);
        public double Sound => AverageOf(GetRatedParts(), p => p.Sound);
        public double Opening => OpeningAverage(GetRatedParts());
        public double Ending => EndingAverage(GetRatedParts());
        public double Emotions => AverageOf(GetRatedParts(), p => p.Emotions);
        public double Originality => AverageOf(GetRatedParts(), p => p.Originality);
        public double Finale => AverageOf(GetRatedParts(), p => p.Finale);

        /// <summary>
        /// Общий балл: среднее восьми критериев + опенинги и эндинги (если они есть).
        /// Список частей собирается один раз на весь расчёт.
        /// </summary>
        public double Score
        {
            get
            {
                var parts = GetRatedParts();
                double sum = AverageOf(parts, p => p.Story) + AverageOf(parts, p => p.MainCharacters)
                           + AverageOf(parts, p => p.Characters) + AverageOf(parts, p => p.Visual)
                           + AverageOf(parts, p => p.Sound) + AverageOf(parts, p => p.Emotions)
                           + AverageOf(parts, p => p.Originality) + AverageOf(parts, p => p.Finale);
                int count = 8;

                double opening = OpeningAverage(parts);
                if (opening > 0) { sum += opening; count++; }

                double ending = EndingAverage(parts);
                if (ending > 0) { sum += ending; count++; }

                return count > 0 ? sum / count : 1;
            }
        }

        private double adjustedScore;
        /// <summary>Итоговый балл с поправкой для разрешения равенств в топе (определяет место).</summary>
        public double AdjustedScore
        {
            get => adjustedScore;
            set
            {
                if (adjustedScore == value) return; // не шлём лишние уведомления
                adjustedScore = value;
                OnPropertyChanged();
            }
        }

        private int rank;
        /// <summary>Место в топе (1 — лучшее).</summary>
        public int Rank
        {
            get => rank;
            set
            {
                if (rank == value) return; // при пересортировке большинство мест не меняется
                rank = value;
                OnPropertyChanged();
            }
        }

        public List<Season> Seasons { get; set; } = new();
        public List<Movie> Movies { get; set; } = new();
        public List<Ova> Ovas { get; set; } = new();
        /// <summary>ONA (Original Net Animation — аниме, выпущенное в интернете). В старых файлах данных поля нет — подставляется пустой список.</summary>
        public List<Ona> Onas { get; set; } = new();

        // ───────────── Предварительная (временная) оценка ─────────────
        // Чтение — среднее по частям; запись — сдвиг оценок всех частей на одну и ту же дельту (в пределах 1..10).
        [JsonIgnore] public double TempStory { get => GetTempAverage(p => p.TempStory); set => SetTempAverage(p => p.TempStory, (p, v) => p.TempStory = v, value); }
        [JsonIgnore] public double TempMainCharacters { get => GetTempAverage(p => p.TempMainCharacters); set => SetTempAverage(p => p.TempMainCharacters, (p, v) => p.TempMainCharacters = v, value); }
        [JsonIgnore] public double TempCharacters { get => GetTempAverage(p => p.TempCharacters); set => SetTempAverage(p => p.TempCharacters, (p, v) => p.TempCharacters = v, value); }
        [JsonIgnore] public double TempVisual { get => GetTempAverage(p => p.TempVisual); set => SetTempAverage(p => p.TempVisual, (p, v) => p.TempVisual = v, value); }
        [JsonIgnore] public double TempSound { get => GetTempAverage(p => p.TempSound); set => SetTempAverage(p => p.TempSound, (p, v) => p.TempSound = v, value); }
        [JsonIgnore] public double TempEmotions { get => GetTempAverage(p => p.TempEmotions); set => SetTempAverage(p => p.TempEmotions, (p, v) => p.TempEmotions = v, value); }
        [JsonIgnore] public double TempOriginality { get => GetTempAverage(p => p.TempOriginality); set => SetTempAverage(p => p.TempOriginality, (p, v) => p.TempOriginality = v, value); }
        [JsonIgnore] public double TempFinale { get => GetTempAverage(p => p.TempFinale); set => SetTempAverage(p => p.TempFinale, (p, v) => p.TempFinale = v, value); }
        [JsonIgnore] public double TempOpening { get => GetTempOpeningAverage(); set => SetTempOpeningAverage(value); }
        [JsonIgnore] public double TempEnding { get => GetTempEndingAverage(); set => SetTempEndingAverage(value); }

        // ───────────── Вспомогательные расчёты ─────────────

        /// <summary>
        /// Среднее значение критерия по частям. Критерий передаётся делегатом — это заменяет
        /// прежний поиск свойства через рефлексию (GetProperty/GetValue), который выполнялся
        /// для каждой части при каждом обращении и сильно тормозил на больших списках.
        /// Для тайтла без частей возвращается 1 (минимальная оценка).
        /// </summary>
        private static double AverageOf(List<PartBase> parts, Func<PartBase, double> selector)
        {
            if (parts.Count == 0) return 1;
            double sum = 0;
            foreach (var part in parts)
                sum += selector(part);
            return sum / parts.Count;
        }

        /// <summary>Средняя оценка опенингов по всем частям (взвешена количеством опенингов); 0, если их нет.</summary>
        private static double OpeningAverage(List<PartBase> parts)
        {
            if (parts.Count == 0) return 0;
            double totalSum = 0;
            int totalCount = 0;
            foreach (var part in parts)
            {
                totalSum += part.OpeningScores.Sum();
                totalCount += part.OpeningCount;
            }
            return totalCount > 0 ? totalSum / totalCount : 0;
        }

        /// <summary>Средняя оценка эндингов по всем частям (взвешена количеством эндингов); 0, если их нет.</summary>
        private static double EndingAverage(List<PartBase> parts)
        {
            if (parts.Count == 0) return 0;
            double totalSum = 0;
            int totalCount = 0;
            foreach (var part in parts)
            {
                totalSum += part.EndingScores.Sum();
                totalCount += part.EndingCount;
            }
            return totalCount > 0 ? totalSum / totalCount : 0;
        }

        /// <summary>Возвращает все части тайтла одним списком: сезоны, фильмы, OVA, ONA.</summary>
        public List<PartBase> GetAllParts()
        {
            var parts = new List<PartBase>(Seasons.Count + Movies.Count + Ovas.Count + Onas.Count);
            parts.AddRange(Seasons);
            parts.AddRange(Movies);
            parts.AddRange(Ovas);
            parts.AddRange(Onas);
            return parts;
        }

        /// <summary>
        /// Истина, если OVA и ONA участвуют в оценке. Обычно они только указываются в составе тайтла
        /// (и учитываются в плане, топе и статистике), но не оцениваются. Исключение — тайтл, у которого
        /// нет ни сезонов, ни фильмов: иначе его вообще нечем было бы оценить.
        /// </summary>
        [JsonIgnore]
        public bool OvaAndOnaAreRated => Seasons.Count == 0 && Movies.Count == 0;

        /// <summary>
        /// Части, которые оцениваются и входят в итоговый балл: сезоны и фильмы
        /// (а для тайтла без сезонов и фильмов — OVA и ONA, см. <see cref="OvaAndOnaAreRated"/>).
        /// </summary>
        public List<PartBase> GetRatedParts()
        {
            if (OvaAndOnaAreRated)
                return GetAllParts();

            var parts = new List<PartBase>(Seasons.Count + Movies.Count);
            parts.AddRange(Seasons);
            parts.AddRange(Movies);
            return parts;
        }

        private double GetTempAverage(Func<PartBase, double> getter)
            => AverageOf(GetRatedParts(), getter);

        /// <summary>
        /// Устанавливает среднюю временную оценку: вычисляет разницу с текущим средним и сдвигает
        /// оценку каждой части на эту разницу (результат ограничивается диапазоном 1..10).
        /// </summary>
        private void SetTempAverage(Func<PartBase, double> getter, Action<PartBase, double> setter, double value)
        {
            var parts = GetRatedParts();
            if (parts.Count == 0) return;

            double delta = value - AverageOf(parts, getter);
            foreach (var p in parts)
                setter(p, Math.Clamp(getter(p) + delta, 1, 10));
        }

        private double GetTempOpeningAverage()
        {
            var parts = GetAllParts(); if (parts.Count == 0) return 0;
            double totalSum = 0; int totalCount = 0;
            foreach (var part in parts)
            { totalSum += part.TempOpeningScores.Sum(); totalCount += part.OpeningCount; }
            return totalCount > 0 ? totalSum / totalCount : 0;
        }

        private void SetTempOpeningAverage(double value)
        {
            var parts = GetAllParts(); if (parts.Count == 0) return;
            double cur = GetTempOpeningAverage(); double delta = value - cur;
            foreach (var part in parts)
                for (int i = 0; i < part.TempOpeningScores.Count; i++)
                    part.TempOpeningScores[i] = Math.Clamp(part.TempOpeningScores[i] + delta, 1, 10);
        }

        private double GetTempEndingAverage()
        {
            var parts = GetAllParts(); if (parts.Count == 0) return 0;
            double totalSum = 0; int totalCount = 0;
            foreach (var part in parts)
            { totalSum += part.TempEndingScores.Sum(); totalCount += part.EndingCount; }
            return totalCount > 0 ? totalSum / totalCount : 0;
        }

        private void SetTempEndingAverage(double value)
        {
            var parts = GetAllParts(); if (parts.Count == 0) return;
            double cur = GetTempEndingAverage(); double delta = value - cur;
            foreach (var part in parts)
                for (int i = 0; i < part.TempEndingScores.Count; i++)
                    part.TempEndingScores[i] = Math.Clamp(part.TempEndingScores[i] + delta, 1, 10);
        }

        /// <summary>Переносит временные (предварительные) оценки в основные для всех частей тайтла.</summary>
        public void ApplyTempScores()
        {
            foreach (var part in GetAllParts())
            {
                part.Story = part.TempStory;
                part.MainCharacters = part.TempMainCharacters;
                part.Characters = part.TempCharacters;
                part.Visual = part.TempVisual;
                part.Sound = part.TempSound;
                part.Emotions = part.TempEmotions;
                part.Originality = part.TempOriginality;
                part.Finale = part.TempFinale;
                for (int i = 0; i < part.OpeningScores.Count; i++)
                    part.OpeningScores[i] = part.TempOpeningScores[i];
                for (int i = 0; i < part.EndingScores.Count; i++)
                    part.EndingScores[i] = part.TempEndingScores[i];
            }
        }

        [JsonIgnore] public int TotalEpisodeCount => Seasons.Sum(s => s.EpisodeCount) + Movies.Count + Ovas.Sum(o => o.EpisodeCount) + Onas.Sum(o => o.EpisodeCount);
        [JsonIgnore]
        public string YearsDisplay
        {
            get
            {
                // SortedSet сразу даёт уникальные годы по возрастанию (вместо Distinct + OrderBy)
                var years = new SortedSet<int>();
                foreach (var s in Seasons) years.Add(s.Year);
                foreach (var m in Movies) years.Add(m.Year);
                foreach (var o in Ovas) years.Add(o.Year);
                foreach (var o in Onas) years.Add(o.Year);
                return years.Count > 0 ? string.Join(", ", years) : "—";
            }
        }
        [JsonIgnore] public string EpisodesDisplay => "";
    }

    /// <summary>
    /// Базовый класс «части» тайтла (сезон, фильм, OVA, ONA): восемь оценок по критериям, оценки опенингов/эндингов
    /// и их временные (предварительные) аналоги.
    /// </summary>
    public abstract class PartBase : INotifyPropertyChanged
    {
        private double story = 1, mainCharacters = 1, characters = 1, visual = 1, sound = 1;
        private double emotions = 1, originality = 1, finale = 1;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public double Story { get => story; set { story = value; OnPropertyChanged(); } }
        public double MainCharacters { get => mainCharacters; set { mainCharacters = value; OnPropertyChanged(); } }
        public double Characters { get => characters; set { characters = value; OnPropertyChanged(); } }
        public double Visual { get => visual; set { visual = value; OnPropertyChanged(); } }
        public double Sound { get => sound; set { sound = value; OnPropertyChanged(); } }
        public double Emotions { get => emotions; set { emotions = value; OnPropertyChanged(); } }
        public double Originality { get => originality; set { originality = value; OnPropertyChanged(); } }
        public double Finale { get => finale; set { finale = value; OnPropertyChanged(); } }

        public double Opening
        {
            get => OpeningScores.Count > 0 ? OpeningScores.Average() : 0;
            set
            {
                for (int i = 0; i < OpeningScores.Count; i++)
                    OpeningScores[i] = value;
                OnPropertyChanged();
            }
        }
        public double Ending
        {
            get => EndingScores.Count > 0 ? EndingScores.Average() : 0;
            set
            {
                for (int i = 0; i < EndingScores.Count; i++)
                    EndingScores[i] = value;
                OnPropertyChanged();
            }
        }

        private double tempStory = 1;
        public double TempStory { get => tempStory; set { tempStory = value; OnPropertyChanged(); } }
        private double tempMainCharacters = 1;
        public double TempMainCharacters { get => tempMainCharacters; set { tempMainCharacters = value; OnPropertyChanged(); } }
        private double tempCharacters = 1;
        public double TempCharacters { get => tempCharacters; set { tempCharacters = value; OnPropertyChanged(); } }
        private double tempVisual = 1;
        public double TempVisual { get => tempVisual; set { tempVisual = value; OnPropertyChanged(); } }
        private double tempSound = 1;
        public double TempSound { get => tempSound; set { tempSound = value; OnPropertyChanged(); } }
        private double tempEmotions = 1;
        public double TempEmotions { get => tempEmotions; set { tempEmotions = value; OnPropertyChanged(); } }
        private double tempOriginality = 1;
        public double TempOriginality { get => tempOriginality; set { tempOriginality = value; OnPropertyChanged(); } }
        private double tempFinale = 1;
        public double TempFinale { get => tempFinale; set { tempFinale = value; OnPropertyChanged(); } }

        public double TempOpening
        {
            get => TempOpeningScores.Count > 0 ? TempOpeningScores.Average() : 0;
            set
            {
                for (int i = 0; i < TempOpeningScores.Count; i++)
                    TempOpeningScores[i] = value;
                OnPropertyChanged();
            }
        }
        public double TempEnding
        {
            get => TempEndingScores.Count > 0 ? TempEndingScores.Average() : 0;
            set
            {
                for (int i = 0; i < TempEndingScores.Count; i++)
                    TempEndingScores[i] = value;
                OnPropertyChanged();
            }
        }

        private int openingCount = 0;
        public int OpeningCount
        {
            get => openingCount;
            set
            {
                int min = this is Season ? 1 : 0;
                if (value < min) value = min;
                if (openingCount == value) return;
                openingCount = value;
                AdjustOpeningScoresList();
                OnPropertyChanged();
            }
        }
        private int endingCount = 0;
        public int EndingCount
        {
            get => endingCount;
            set
            {
                int min = this is Season ? 1 : 0;
                if (value < min) value = min;
                if (endingCount == value) return;
                endingCount = value;
                AdjustEndingScoresList();
                OnPropertyChanged();
            }
        }

        private ObservableCollection<double> _openingScores = new();
        public ObservableCollection<double> OpeningScores { get => _openingScores; set { _openingScores = value; OnPropertyChanged(); } }
        private ObservableCollection<double> _endingScores = new();
        public ObservableCollection<double> EndingScores { get => _endingScores; set { _endingScores = value; OnPropertyChanged(); } }
        private ObservableCollection<double> _tempOpeningScores = new();
        public ObservableCollection<double> TempOpeningScores { get => _tempOpeningScores; set { _tempOpeningScores = value; OnPropertyChanged(); } }
        private ObservableCollection<double> _tempEndingScores = new();
        public ObservableCollection<double> TempEndingScores { get => _tempEndingScores; set { _tempEndingScores = value; OnPropertyChanged(); } }

        public void AdjustOpeningScoresList()
        {
            while (OpeningScores.Count < OpeningCount) OpeningScores.Add(1);
            while (OpeningScores.Count > OpeningCount) OpeningScores.RemoveAt(OpeningScores.Count - 1);
            while (TempOpeningScores.Count < OpeningCount) TempOpeningScores.Add(1);
            while (TempOpeningScores.Count > OpeningCount) TempOpeningScores.RemoveAt(TempOpeningScores.Count - 1);
        }

        public void AdjustEndingScoresList()
        {
            while (EndingScores.Count < EndingCount) EndingScores.Add(1);
            while (EndingScores.Count > EndingCount) EndingScores.RemoveAt(EndingScores.Count - 1);
            while (TempEndingScores.Count < EndingCount) TempEndingScores.Add(1);
            while (TempEndingScores.Count > EndingCount) TempEndingScores.RemoveAt(TempEndingScores.Count - 1);
        }

        public void AutoSetOpeningEndingCount(int episodeCount)
        {
            if (episodeCount <= 12) { OpeningCount = 1; EndingCount = 1; }
            else if (episodeCount <= 24) { OpeningCount = 2; EndingCount = 2; }
            else if (episodeCount <= 36) { OpeningCount = 3; EndingCount = 3; }
            else { OpeningCount = 4; EndingCount = 4; }
        }

        public void ApplyTempScores()
        {
            Story = TempStory; MainCharacters = TempMainCharacters; Characters = TempCharacters;
            Visual = TempVisual; Sound = TempSound; Emotions = TempEmotions;
            Originality = TempOriginality; Finale = TempFinale;
            for (int i = 0; i < OpeningScores.Count; i++) OpeningScores[i] = TempOpeningScores[i];
            for (int i = 0; i < EndingScores.Count; i++) EndingScores[i] = TempEndingScores[i];
        }

        public void ResetScores()
        {
            Story = 1; MainCharacters = 1; Characters = 1; Visual = 1; Sound = 1;
            Emotions = 1; Originality = 1; Finale = 1;
            for (int i = 0; i < OpeningScores.Count; i++) OpeningScores[i] = 1;
            for (int i = 0; i < EndingScores.Count; i++) EndingScores[i] = 1;
            TempStory = 1; TempMainCharacters = 1; TempCharacters = 1; TempVisual = 1; TempSound = 1;
            TempEmotions = 1; TempOriginality = 1; TempFinale = 1;
            for (int i = 0; i < TempOpeningScores.Count; i++) TempOpeningScores[i] = 1;
            for (int i = 0; i < TempEndingScores.Count; i++) TempEndingScores[i] = 1;
        }
    }

    /// <summary>Сезон: минимум один опенинг и эндинг; их число подбирается автоматически по числу серий.</summary>
    public class Season : PartBase
    {
        public Season()
        {
            OpeningCount = 1;
            EndingCount = 1;
            AdjustOpeningScoresList();
            AdjustEndingScoresList();
        }

        private int episodeCount;
        public int EpisodeCount
        {
            get => episodeCount;
            set { episodeCount = value; AutoSetOpeningEndingCount(value); OnPropertyChanged(); }
        }
        private int year;
        public int Year { get => year; set { year = value; OnPropertyChanged(); } }
    }

    /// <summary>Полнометражный фильм: по умолчанию без опенинга и эндинга.</summary>
    public class Movie : PartBase
    {
        public Movie()
        {
            OpeningCount = 0;
            EndingCount = 0;
            AdjustOpeningScoresList();
            AdjustEndingScoresList();
        }

        private int year;
        public int Year { get => year; set { year = value; OnPropertyChanged(); } }
    }

    /// <summary>OVA (специальные эпизоды): по умолчанию без опенинга и эндинга.</summary>
    public class Ova : PartBase
    {
        public Ova()
        {
            OpeningCount = 0;
            EndingCount = 0;
            AdjustOpeningScoresList();
            AdjustEndingScoresList();
        }

        private int episodeCount;
        public int EpisodeCount { get => episodeCount; set { episodeCount = value; OnPropertyChanged(); } }
        private int year;
        public int Year { get => year; set { year = value; OnPropertyChanged(); } }
    }

    /// <summary>ONA (Original Net Animation): по умолчанию без опенинга и эндинга. Не оценивается — учитывается только в составе тайтла.</summary>
    public class Ona : PartBase
    {
        public Ona()
        {
            OpeningCount = 0;
            EndingCount = 0;
            AdjustOpeningScoresList();
            AdjustEndingScoresList();
        }

        private int episodeCount;
        public int EpisodeCount { get => episodeCount; set { episodeCount = value; OnPropertyChanged(); } }
        private int year;
        public int Year { get => year; set { year = value; OnPropertyChanged(); } }
    }
}