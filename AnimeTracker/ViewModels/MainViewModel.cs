﻿using AnimeTracker.Commands;
using AnimeTracker.Models;
using AnimeTracker.Services;
using AnimeTracker.Views;
using LiveCharts;
using LiveCharts.Wpf;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace AnimeTracker.ViewModels
{
    /// <summary>
    /// Минимальный файловый логгер: пишет ошибки в %LocalAppData%\AnimeTracker\error.log.
    /// Любые сбои самого логгера намеренно подавляются, чтобы логирование никогда не роняло приложение.
    /// </summary>
    public static class Logger
    {
        private static readonly string LogFilePath;
        static Logger()
        {
            string appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AnimeTracker");
            if (!Directory.Exists(appData))
                Directory.CreateDirectory(appData);
            LogFilePath = Path.Combine(appData, "error.log");
        }
        public static void Log(string message)
        {
            try { File.AppendAllText(LogFilePath, $"{DateTime.Now}: {message}\n"); }
            catch { }
        }
    }

    // Общая форма для всех трёх карточек-списков ("Топ-10 аниме", "Средний балл
    // по жанрам", "Топ-5 жанров"): название сверху (обычный TextBlock, никогда
    // не обрезается — переносится на следующую строку при нехватке места) и
    // полоса снизу, отрисованные в XAML вручную, без LiveCharts. FillPortion/
    // RemainingPortion — это просто пропорции для двух Star-колонок Grid (сумма
    // не обязана быть конкретным числом, важно лишь соотношение), поэтому один
    // и тот же DataTemplate (ScoreBarItemTemplate в XAML) подходит и баллам
    // 0..10, и произвольным счётчикам жанров — каждый класс сам решает, как
    // считать свою пропорцию и как форматировать отображаемое число.

    // "Топ-10 аниме" и "Средний балл по жанрам" — балл по шкале 0..10.
    public class TopAnimeBarItem
    {
        public string Title { get; set; } = "";
        public double Score { get; set; }
        // Больше не применяем Math.Max(0.01, Score) — при Score=10 остаток будет 0,
        // и полоса полностью заполнит колонку.
        public double FillPortion => Score;
        public double RemainingPortion => 10 - Score;
        public string DisplayValue => Score.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
    }

    public class GenreBarItem
    {
        public string Title { get; set; } = "";
        public double Score { get; set; }
        public double FillPortion => Score;
        public double RemainingPortion => 10 - Score;
        public string DisplayValue => Score.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
    }

    // "Топ-5 жанров" — количество аниме в жанре, шкалы 0..10 тут нет (счётчик
    // может быть любым), поэтому пропорция считается относительно MaxCount —
    // наибольшего количества среди показанных жанров (у лидера полоса всегда
    // заполнена целиком, у остальных — пропорционально меньше).
    public class GenreCountBarItem
    {
        public string Title { get; set; } = "";
        public int Count { get; set; }
        public int MaxCount { get; set; }
        public double FillPortion => Count;
        public double RemainingPortion => MaxCount - Count;
        public string DisplayValue => Count.ToString();
    }

    /// <summary>
    /// <see cref="ObservableCollection{T}"/> с операцией массовой замены содержимого.
    /// Обычные <c>Clear()</c> + <c>Add()</c> в цикле порождают N+1 уведомлений, и привязанный DataGrid
    /// каждый раз пересчитывает раскладку. <see cref="ReplaceAll"/> меняет данные «тихо» и отправляет
    /// ОДНО уведомление Reset — на сотнях записей это ускоряет обновление списков в разы.
    /// Наследование от ObservableCollection сохраняет полную совместимость с существующими привязками.
    /// </summary>
    public class BulkObservableCollection<T> : ObservableCollection<T>
    {
        public void ReplaceAll(IEnumerable<T> items)
        {
            CheckReentrancy();
            var snapshot = items.ToList(); // защита на случай, если источник — сама эта коллекция
            Items.Clear();
            foreach (var item in snapshot)
                Items.Add(item);

            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }

    /// <summary>
    /// Главная модель представления (MVVM) приложения Anime Tracker.
    /// Отвечает за:
    ///  • списки аниме («План», «Топ») и их фильтрацию/сортировку;
    ///  • процесс оценивания и расчёт позиций в топе;
    ///  • статистику и данные для диаграмм (с ленивым кэшированием);
    ///  • загрузку/сохранение данных в JSON;
    ///  • локализованные коллекции (жанры, статусы).
    /// </summary>
    public class MainViewModel : INotifyPropertyChanged
    {
        private static MainViewModel? _instance;
        public static MainViewModel Instance
        {
            get => _instance ?? throw new InvalidOperationException("MainViewModel не инициализирован.");
            set => _instance = value;
        }

        /// <summary>Все аниме, которые ещё не оценены (статусы «План» и «Смотрю»).</summary>
        public ObservableCollection<Anime> AnimeList { get; set; } = new BulkObservableCollection<Anime>();

        /// <summary>Оценённые аниме, отсортированные по убыванию итогового балла.</summary>
        public ObservableCollection<Anime> TopList { get; set; } = new BulkObservableCollection<Anime>();

        // Список, который показывает вкладка «План». Обновляется одной операцией (см. RefreshPlanView).
        private ObservableCollection<Anime> _planList = new BulkObservableCollection<Anime>();
        public ObservableCollection<Anime> PlanList
        {
            get => _planList;
            set { _planList = value; OnPropertyChanged(); }
        }

        private readonly Random rand = new();
        private bool _isReEvaluating = false;
        private bool _isResettingData = false;
        private bool _isLoadingData = false;
        private readonly object _loadLock = new();

        private static readonly string[] _genreOptions = new[]
        {
            "Ангелы", "Антиутопия", "Безумие", "Бисёнен", "Божества",
            "Боевые искусства", "Вампиры", "Виртуальная реальность", "Военное",
            "Воры", "Гарем", "Гонки", "Гурман", "Демоны", "Детектив",
            "Дзёсэй", "Драконы", "Драма", "Игры", "Инопланетяне", "Исекай",
            "Исторический", "Киберпанк", "Кодомо", "Комедия", "Космос",
            "Лоликон", "Любовный треугольник", "Магия", "Махо-сёдзё",
            "Мафия", "Медицина", "Меха", "Мистика", "Музыка",
            "Нелинейный Сюжет", "Ниндзя", "Параллельный мир", "Пародия",
            "Перестрелки", "Повседневность", "Полиция", "Пост-апокалипсис", "Приключения",
            "Прокси бои", "Психологический триллер", "Психология",
            "Романтика", "Самураи", "Сверхъестественное",
            "Сёдзё", "Сёдзё-ай", "Спокон", "Спорт", "Сражения на мечах",
            "Стимпанк", "Супер способности", "Сэйнэн", "Сёнэн",
            "Тёмное Фэнтези", "Трагедия", "Триллер", "Убийцы", "Ужасы",
            "Фантастика", "Фэнтези", "Школа", "Экшен", "Эльфы",
            "Эротика", "Этти", "Юри", "Яой"
        };

        private static readonly List<string> _genreOptionsWithEmpty = new[] { "" }.Concat(_genreOptions).ToList();
        public static List<string> GenreOptions => _genreOptionsWithEmpty;
        public static List<string> AdditionalGenreOptions => _genreOptionsWithEmpty;

        public event PropertyChangedEventHandler? PropertyChanged;
        public virtual void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private List<string> _localizedGenreOptions = [];
        private List<string> _localizedAdditionalGenreOptions = [];
        private List<string> _localizedStatusList = [];

        public List<string> LocalizedGenreOptions
        {
            get => _localizedGenreOptions;
            private set { _localizedGenreOptions = value; OnPropertyChanged(); }
        }

        public List<string> LocalizedAdditionalGenreOptions
        {
            get => _localizedAdditionalGenreOptions;
            private set { _localizedAdditionalGenreOptions = value; OnPropertyChanged(); }
        }

        public List<string> LocalizedStatusList
        {
            get => _localizedStatusList;
            private set { _localizedStatusList = value; OnPropertyChanged(); }
        }

        private readonly ObservableCollection<string> _randomGenreOptions = new();
        public ObservableCollection<string> RandomGenreOptions => _randomGenreOptions;

        // true — кэш статистики нужно сбросить при следующем обращении. Выставляется при любом изменении данных
        // (в том числе автоматически при изменении состава TopList), снимается в EnsureStatisticsFresh().
        private bool _isStatisticsDirty = true;
        private List<GenreStat>? _cachedTopGenres;
        private List<GenreStat>? _cachedAvgScoreByGenre;
        private List<YearTopEntry>? _cachedTopByYear;
        private List<Anime>? _cachedTopTen;
        private string? _cachedFavoriteGenre;
        private string? _cachedLongestAnime;
        private string? _cachedShortestAnime;
        private List<string>? _cachedRecommendations;
        private double? _cachedAvgEpisodesPerSeason;
        private double? _cachedMedianEpisodesPerSeason;
        private int? _cachedWatchedPercent;
        private int? _cachedWatchingPercent;
        private int? _cachedPlannedPercent;

        // Задержка (debounce) перед применением поиска: список перестраивается не на каждую
        // нажатую клавишу, а когда пользователь на мгновение остановился.
        private const int SearchDebounceMs = 300;
        private DispatcherTimer? _searchTimer;
        private DispatcherTimer? _searchTimerTop;

        /// <summary>
        /// Перезапускает таймер задержки. Таймер создаётся один раз и переиспользуется
        /// (раньше на каждый символ создавался новый DispatcherTimer с новой подпиской на Tick).
        /// </summary>
        private static void RestartDebounce(ref DispatcherTimer? timer, Action action)
        {
            if (timer == null)
            {
                var created = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(SearchDebounceMs) };
                created.Tick += (s, e) =>
                {
                    created.Stop();
                    action();
                };
                timer = created;
            }
            timer.Stop();
            timer.Start();
        }

        private string _searchText = "";
        /// <summary>Строка поиска на вкладке «План» (применяется с задержкой).</summary>
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText == value) return;
                _searchText = value;
                RestartDebounce(ref _searchTimer, RefreshPlanView);
                OnPropertyChanged();
            }
        }

        public bool IsAddingAnime { get; set; } = false;

        /// <summary>
        /// Сбрасывает кэш статистики ОДИН раз после изменения данных.
        /// Раньше флаг «грязности» никогда не снимался, поэтому каждое обращение к любому
        /// свойству статистики пересчитывало её заново (а при обновлении UI таких обращений десятки).
        /// Теперь значения считаются лениво и ровно один раз до следующего изменения данных.
        /// </summary>
        private void EnsureStatisticsFresh()
        {
            if (!_isStatisticsDirty) return;
            _isStatisticsDirty = false;

            _cachedTopGenres = null;
            _cachedAvgScoreByGenre = null;
            _cachedTopByYear = null;
            _cachedTopTen = null;
            _cachedFavoriteGenre = null;
            _cachedLongestAnime = null;
            _cachedShortestAnime = null;
            _cachedRecommendations = null;
            _cachedAvgEpisodesPerSeason = null;
            _cachedMedianEpisodesPerSeason = null;
            _cachedWatchedPercent = null;
            _cachedWatchingPercent = null;
            _cachedPlannedPercent = null;
        }

        public List<GenreStat> TopGenres
        {
            get
            {
                EnsureStatisticsFresh();
                if (_cachedTopGenres == null)
                    _cachedTopGenres = ComputeTopGenres();
                return _cachedTopGenres;
            }
        }

        public List<GenreStat> AvgScoreByGenre
        {
            get
            {
                EnsureStatisticsFresh();
                if (_cachedAvgScoreByGenre == null)
                    _cachedAvgScoreByGenre = ComputeAvgScoreByGenre();
                return _cachedAvgScoreByGenre;
            }
        }

        public List<YearTopEntry> TopByYear
        {
            get
            {
                EnsureStatisticsFresh();
                if (_cachedTopByYear == null)
                    _cachedTopByYear = ComputeTopByYear();
                return _cachedTopByYear;
            }
        }

        public List<Anime> TopTen
        {
            get
            {
                EnsureStatisticsFresh();
                if (_cachedTopTen == null)
                    _cachedTopTen = TopList.Take(10).ToList();
                return _cachedTopTen;
            }
        }

        public string FavoriteGenre
        {
            get
            {
                EnsureStatisticsFresh();
                if (_cachedFavoriteGenre == null)
                    _cachedFavoriteGenre = ComputeFavoriteGenre();
                return _cachedFavoriteGenre;
            }
        }

        public string LongestAnime
        {
            get
            {
                EnsureStatisticsFresh();
                if (_cachedLongestAnime == null)
                    _cachedLongestAnime = ComputeLongestAnime();
                return _cachedLongestAnime;
            }
        }

        public string ShortestAnime
        {
            get
            {
                EnsureStatisticsFresh();
                if (_cachedShortestAnime == null)
                    _cachedShortestAnime = ComputeShortestAnime();
                return _cachedShortestAnime;
            }
        }

        public List<string> Recommendations
        {
            get
            {
                EnsureStatisticsFresh();
                if (_cachedRecommendations == null)
                    _cachedRecommendations = ComputeRecommendations();
                return _cachedRecommendations;
            }
        }

        public double AvgEpisodesPerSeason
        {
            get
            {
                EnsureStatisticsFresh();
                if (_cachedAvgEpisodesPerSeason == null)
                    _cachedAvgEpisodesPerSeason = ComputeAvgEpisodesPerSeason();
                return _cachedAvgEpisodesPerSeason.Value;
            }
        }

        public double MedianEpisodesPerSeason
        {
            get
            {
                EnsureStatisticsFresh();
                if (_cachedMedianEpisodesPerSeason == null)
                    _cachedMedianEpisodesPerSeason = ComputeMedianEpisodesPerSeason();
                return _cachedMedianEpisodesPerSeason.Value;
            }
        }

        public int WatchedPercent
        {
            get
            {
                EnsureStatisticsFresh();
                if (_cachedWatchedPercent == null)
                    _cachedWatchedPercent = ComputeWatchedPercent();
                return _cachedWatchedPercent.Value;
            }
        }

        public int WatchingPercent
        {
            get
            {
                EnsureStatisticsFresh();
                if (_cachedWatchingPercent == null)
                    _cachedWatchingPercent = ComputeWatchingPercent();
                return _cachedWatchingPercent.Value;
            }
        }

        public int PlannedPercent
        {
            get
            {
                EnsureStatisticsFresh();
                if (_cachedPlannedPercent == null)
                    _cachedPlannedPercent = ComputePlannedPercent();
                return _cachedPlannedPercent.Value;
            }
        }

        public int WatchedCount => TopList.Count;
        public int PlannedCount => AnimeList.Count(a => a.Status != "Просмотрено" && a.Status != "Watched");
        public int WatchingCount => AnimeList.Count(a => a.Status == "Смотрю" || a.Status == "Watching");
        public int TotalEpisodes => TopList.Sum(a => a.Seasons.Sum(s => s.EpisodeCount) + a.Movies.Count);
        /// <summary>
        /// Общее время просмотра. Оценка: серия — 25 минут, фильм — 90 минут.
        /// Суммы считаются один раз (раньше — по два прохода по TopList на каждую ветку).
        /// </summary>
        public string TotalTimeString
        {
            get
            {
                int episodes = TopList.Sum(a => a.Seasons.Sum(s => s.EpisodeCount));
                int movies = TopList.Sum(a => a.Movies.Count);
                double hours = (episodes * 25 + movies * 90) / 60.0;
                string unit = LocalizationManager.CurrentLanguage == "English" ? "h" : "ч";
                return $"{hours:F1} {unit}";
            }
        }

        private SeriesCollection _statusSeries = new();
        public SeriesCollection StatusSeries
        {
            get => _statusSeries;
            set { _statusSeries = value; OnPropertyChanged(); }
        }

        // "Топ-5 жанров" — тот же нативный дизайн (без LiveCharts), что у двух
        // других карточек-списков, только горизонтальные полосы вместо
        // вертикальных: название жанра сверху, полоса-счётчик снизу.
        private List<GenreCountBarItem> _topGenresBarItems = new();
        public List<GenreCountBarItem> TopGenresBarItems
        {
            get => _topGenresBarItems;
            set { _topGenresBarItems = value; OnPropertyChanged(); }
        }

        private SeriesCollection _yearTrendSeries = new();
        public SeriesCollection YearTrendSeries
        {
            get => _yearTrendSeries;
            set { _yearTrendSeries = value; OnPropertyChanged(); }
        }

        // "Топ-10 аниме" больше не рисуется через LiveCharts (ColumnSeries/RowSeries) —
        // после долгой борьбы с обрезкой/поворотом подписей и обрезкой текста заменил
        // диаграмму на обычный список: название крупным текстом сверху, под ним —
        // полоса-индикатор балла. Название всегда видно целиком, поэтому подсказка
        // при наведении (тултип) больше не нужна — сама проблема, для которой она
        // делалась, снята дизайном.
        private List<TopAnimeBarItem> _topAnimeBarItems = new();
        public List<TopAnimeBarItem> TopAnimeBarItems
        {
            get => _topAnimeBarItems;
            set { _topAnimeBarItems = value; OnPropertyChanged(); }
        }

        // "Средний балл по жанрам" — тот же дизайн, что у "Топ-10 аниме": название
        // жанра сверху, полоса снизу, без LiveCharts. Построен напрямую из
        // AvgScoreByGenre (уже отсортирован по убыванию балла и ограничен 10
        // жанрами) — раньше список для этой карточки собирался через промежуточное
        // пересечение с Топ-5 жанров (сортировка ПО КОЛИЧЕСТВУ), из-за чего порядок
        // по баллу терялся. Теперь сортировка по баллу сохраняется как есть.
        private List<GenreBarItem> _avgScoreGenreItems = new();
        public List<GenreBarItem> AvgScoreGenreItems
        {
            get => _avgScoreGenreItems;
            set { _avgScoreGenreItems = value; OnPropertyChanged(); }
        }


        private List<string> _yearLabels = new();
        public List<string> YearLabels
        {
            get => _yearLabels;
            set { _yearLabels = value; OnPropertyChanged(); }
        }

        public string Title { get; set; } = "";
        public string Genre { get; set; } = "";
        public string AdditionalGenre1 { get; set; } = "";
        public string AdditionalGenre2 { get; set; } = "";
        public string Description { get; set; } = "";
        public string VoiceOver { get; set; } = "";
        public string SelectedStatus { get; set; } = "План";

        private int seasonsCount;
        public int SeasonsCount
        {
            get => seasonsCount;
            set
            {
                if (value > 100) value = 100;
                if (seasonsCount == value) return;
                seasonsCount = value;
                OnPropertyChanged();
                UpdateSeasonsCollection();
            }
        }

        private int moviesCount;
        public int MoviesCount
        {
            get => moviesCount;
            set
            {
                if (value > 100) value = 100;
                if (moviesCount == value) return;
                moviesCount = value;
                OnPropertyChanged();
                UpdateMoviesCollection();
            }
        }

        private int ovasCount;
        public int OvasCount
        {
            get => ovasCount;
            set
            {
                if (value > 100) value = 100;
                if (ovasCount == value) return;
                ovasCount = value;
                OnPropertyChanged();
                UpdateOvasCollection();
            }
        }

        public ObservableCollection<Season> TempSeasons { get; set; } = [];
        public ObservableCollection<Movie> TempMovies { get; set; } = [];
        public ObservableCollection<Ova> TempOvas { get; set; } = [];

        private void UpdateSeasonsCollection()
        {
            while (TempSeasons.Count < SeasonsCount)
                TempSeasons.Add(new Season());
            while (TempSeasons.Count > SeasonsCount)
                TempSeasons.RemoveAt(TempSeasons.Count - 1);
        }

        private void UpdateMoviesCollection()
        {
            while (TempMovies.Count < MoviesCount)
                TempMovies.Add(new Movie());
            while (TempMovies.Count > MoviesCount)
                TempMovies.RemoveAt(TempMovies.Count - 1);
        }

        private void UpdateOvasCollection()
        {
            while (TempOvas.Count < OvasCount)
                TempOvas.Add(new Ova { EpisodeCount = 1 });
            while (TempOvas.Count > OvasCount)
                TempOvas.RemoveAt(TempOvas.Count - 1);
        }

        private string searchTextTop = "";
        public string SearchTextTop
        {
            get => searchTextTop;
            set
            {
                if (searchTextTop == value) return;
                searchTextTop = value;
                RestartDebounce(ref _searchTimerTop, ApplyTopFilter);
                OnPropertyChanged();
            }
        }

        private string randomText = "Нажми на рандомайзер";
        public string RandomText
        {
            get => randomText;
            set { randomText = value; OnPropertyChanged(); }
        }

        private string selectedRandomGenre = "Все жанры";
        public string SelectedRandomGenre
        {
            get => selectedRandomGenre;
            set { selectedRandomGenre = value; OnPropertyChanged(); }
        }

        private Anime? selectedAnime;
        public Anime? SelectedAnime
        {
            get => selectedAnime;
            set { selectedAnime = value; OnPropertyChanged(); }
        }

        private Anime? selectedForRating;
        public Anime? SelectedForRating
        {
            get => selectedForRating;
            set
            {
                if (selectedForRating == value) return;
                selectedForRating = value;
                OnPropertyChanged(nameof(HasAnimeForRating));
                OnPropertyChanged(nameof(SelectedForRatingParts));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private Anime? _currentRatingAnime;
        public Anime? CurrentRatingAnime
        {
            get => _currentRatingAnime;
            set
            {
                if (_currentRatingAnime == value) return;
                _currentRatingAnime = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasAnimeForRating));
            }
        }

        private Anime? selectedTopAnime;
        public Anime? SelectedTopAnime
        {
            get => selectedTopAnime;
            set { selectedTopAnime = value; OnPropertyChanged(); }
        }

        private int selectedTabIndex;
        public int SelectedTabIndex
        {
            get => selectedTabIndex;
            set
            {
                if (selectedTabIndex == value) return;
                selectedTabIndex = value;
                OnPropertyChanged();
            }
        }

        public bool HasAnimeForRating => SelectedForRating != null || CurrentRatingAnime != null;

        private string _eulaText = "";
        public string EulaText
        {
            get => _eulaText;
            set { _eulaText = value; OnPropertyChanged(); }
        }

        // ───────────── Команды (привязываются из XAML) ─────────────
        public ICommand AddCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand RandomCommand { get; }
        public ICommand AddToTopCommand { get; }
        public ICommand ReEvaluateCommand { get; }
        public ICommand EditTopAnimeCommand { get; }
        public ICommand DeleteFromTopCommand { get; }
        public ICommand EditPlanAnimeCommand { get; }
        public ICommand CancelRatingCommand { get; }

        public static readonly Dictionary<string, Dictionary<int, string>> ScoreDescriptionsRu = new();
        public static readonly Dictionary<string, Dictionary<int, string>> ScoreDescriptionsEn = new();

        /// <summary>
        /// Инициализирует модель: создаёт команды, подписывается на события коллекций и языка,
        /// загружает данные с диска и подготавливает локализованные списки.
        /// </summary>
        public MainViewModel()
        {
            Instance = this;

            UpdateLocalizedCollections();

            AddCommand = new RelayCommand(AddAnime);
            DeleteCommand = new RelayCommand(DeleteAnime);
            RandomCommand = new RelayCommand(RandomAnime);
            AddToTopCommand = new RelayCommand(AddToTop, () => SelectedForRating != null);
            ReEvaluateCommand = new RelayCommand(ReEvaluate, () => SelectedTopAnime != null);
            EditTopAnimeCommand = new RelayCommand(EditTopAnime, () => SelectedTopAnime != null);
            DeleteFromTopCommand = new RelayCommand(DeleteFromTop, () => SelectedTopAnime != null);
            EditPlanAnimeCommand = new RelayCommand(EditPlanAnime, () => SelectedAnime != null);
            CancelRatingCommand = new RelayCommand(CancelRating, () => SelectedForRating != null);

            AnimeList.CollectionChanged += (s, e) =>
            {
                if (_isLoadingData) return;
                if (e.NewItems != null)
                    foreach (Anime a in e.NewItems) SubscribeToAnime(a);
                if (e.OldItems != null)
                    foreach (Anime a in e.OldItems) a.PropertyChanged -= OnAnimePropertyChanged;
            };

            // Любое изменение состава топа делает кэш статистики устаревшим — даже если изменение
            // выполнено напрямую, в обход SortTopList()/UpdateAllStatistics().
            TopList.CollectionChanged += (s, e) => _isStatisticsDirty = true;

            LoadDataFromFile();

            ApplyTopFilter();
            RandomText = LocalizationManager.CurrentLanguage == "English" ? "Press Random" : "Нажми на рандомайзер";
            UpdateRandomGenreOptions();
            SelectedRandomGenre = LocalizationManager.CurrentLanguage == "English" ? "All genres" : "Все жанры";
            EulaText = LoadEulaText();

            LocalizationManager.LanguageChanged += OnLanguageChanged;
        }

        /// <summary>
        /// Подписывает аниме на отслеживание смены статуса. Предварительная отписка гарантирует,
        /// что обработчик не будет подключён дважды.
        /// </summary>
        private void SubscribeToAnime(Anime anime)
        {
            anime.PropertyChanged -= OnAnimePropertyChanged;
            anime.PropertyChanged += OnAnimePropertyChanged;
            _lastKnownStatus.AddOrUpdate(anime, anime.Status ?? "");
        }

        /// <summary>
        /// Возникает, когда аниме из плана могло сменить позицию в списке и его нужно показать пользователю:
        /// после реальной смены статуса (например, «План» → «Смотрю») и после редактирования (изменилось название).
        /// Главное окно плавно прокручивает таблицу к аниме и подсвечивает его.
        /// </summary>
        public event Action<Anime>? PlanAnimeMoved;

        /// <summary>Сообщает окну, что аниме нужно показать в таблице «План» (см. <see cref="PlanAnimeMoved"/>).</summary>
        public void NotifyPlanAnimeMoved(Anime anime) => PlanAnimeMoved?.Invoke(anime);

        // Последний известный статус каждого аниме — позволяет отличить настоящую смену статуса
        // от повторной записи того же значения (например, при сохранении в окне редактирования).
        private readonly ConditionalWeakTable<Anime, string> _lastKnownStatus = new();

        /// <summary>
        /// Очищает список плана, предварительно отписавшись от аниме. Событие Reset не сообщает,
        /// какие элементы удалены, поэтому отписка выполняется вручную — иначе «осиротевшие» объекты
        /// продолжали бы вызывать обработчик модели.
        /// </summary>
        private void ClearAnimeList()
        {
            foreach (var anime in AnimeList)
                anime.PropertyChanged -= OnAnimePropertyChanged;
            AnimeList.Clear();
        }

        /// <summary>
        /// Заменяет содержимое коллекции одним уведомлением (Reset), если она это поддерживает.
        /// Для обычной ObservableCollection работает прежним способом (Clear + Add).
        /// </summary>
        private static void ReplaceItems<T>(ObservableCollection<T> target, IEnumerable<T> items)
        {
            if (target is BulkObservableCollection<T> bulk)
            {
                bulk.ReplaceAll(items);
                return;
            }

            var snapshot = items.ToList();
            target.Clear();
            foreach (var item in snapshot)
                target.Add(item);
        }

        /// <summary>
        /// Пересобирает список вкладки «План»: исключает просмотренные, применяет поиск,
        /// сортирует («Смотрю» выше, затем по названию) и обновляет коллекцию ОДНОЙ операцией.
        /// </summary>
        public void RefreshPlanView()
        {
            if (_isLoadingData) return;

            // Фильтр по поиску применяем до сортировки — сортируется меньше элементов.
            IEnumerable<Anime> query = AnimeList.Where(a => a.Status != "Просмотрено" && a.Status != "Watched");

            string search = SearchText;
            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(a => a.Title.Contains(search, StringComparison.OrdinalIgnoreCase));

            var sorted = query
                .OrderBy(a => a.StatusPriority)
                .ThenBy(a => a.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();

            ReplaceItems(_planList, sorted);
        }

        /// <summary>
        /// Реакция на смену языка: переводит тексты рандомайзера, списки жанров/статусов и лицензию,
        /// уведомляет строки таблиц (жанры) и пересчитывает статистику с локализованными названиями.
        /// </summary>
        private void OnLanguageChanged()
        {
            bool isEnglish = LocalizationManager.CurrentLanguage == "English";
            RandomText = isEnglish ? "Press Random" : "Нажми на рандомайзер";
            OnPropertyChanged(nameof(RandomText));

            string currentRandomGenre = SelectedRandomGenre;
            string allGenresLabel = isEnglish ? "All genres" : "Все жанры";

            UpdateRandomGenreOptions();

            string translatedRandomGenre = (currentRandomGenre == "Все жанры" || currentRandomGenre == "All genres")
                ? allGenresLabel
                : (isEnglish ? GetEnglishGenre(currentRandomGenre) : GetRussianGenre(currentRandomGenre));

            SelectedRandomGenre = RandomGenreOptions.Contains(translatedRandomGenre)
                ? translatedRandomGenre
                : allGenresLabel;

            UpdateLocalizedCollections();

            OnPropertyChanged(nameof(TotalTimeString));
            EulaText = LoadEulaText();
            OnPropertyChanged(nameof(EulaText));

            foreach (var anime in AnimeList)
            {
                anime.RaisePropertyChanged(nameof(anime.Genre));
                anime.RaisePropertyChanged(nameof(anime.AdditionalGenres));
            }
            foreach (var anime in TopList)
            {
                anime.RaisePropertyChanged(nameof(anime.Genre));
                anime.RaisePropertyChanged(nameof(anime.AdditionalGenres));
            }

            RefreshPlanView();
            SortTopList();
            UpdateAllStatistics();
        }

        public static List<string> BuildLocalizedGenreOptions(bool isEnglish)
        {
            var genres = _genreOptions
                .Select(g => isEnglish ? GetEnglishGenre(g) : g)
                .OrderBy(g => g)
                .ToList();
            genres.Insert(0, "");
            return genres;
        }

        public static List<string> BuildLocalizedStatusList(bool isEnglish) => isEnglish
            ? new List<string> { "Plan", "Watching", "Watched" }
            : new List<string> { "План", "Смотрю", "Просмотрено" };

        private void UpdateLocalizedCollections()
        {
            bool isEnglish = LocalizationManager.CurrentLanguage == "English";
            LocalizedGenreOptions = BuildLocalizedGenreOptions(isEnglish);
            LocalizedAdditionalGenreOptions = LocalizedGenreOptions;
            LocalizedStatusList = BuildLocalizedStatusList(isEnglish);
        }

        /// <summary>
        /// Следит за сменой статуса аниме. Если аниме переведено в «Просмотрено» — переносит его из плана
        /// на вкладку оценивания; иначе просто перестраивает список плана.
        /// </summary>
        private void OnAnimePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (IsAddingAnime) return;

            if (e.PropertyName == nameof(Anime.Status))
            {
                var anime = sender as Anime;

                // Выясняем, изменился ли статус на самом деле, и запоминаем новое значение
                bool statusChanged = false;
                if (anime != null)
                {
                    string currentStatus = anime.Status ?? "";
                    statusChanged = !_lastKnownStatus.TryGetValue(anime, out var previousStatus) || previousStatus != currentStatus;
                    _lastKnownStatus.AddOrUpdate(anime, currentStatus);
                }

                if (anime?.Status == "Просмотрено" || anime?.Status == "Watched")
                {
                    anime.ApplyTempScores();
                    if (AnimeList.Contains(anime))
                        AnimeList.Remove(anime);
                    SelectedForRating = anime;
                    CurrentRatingAnime = anime;
                    OnPropertyChanged(nameof(SelectedForRating));
                    OnPropertyChanged(nameof(CurrentRatingAnime));
                    OnPropertyChanged(nameof(HasAnimeForRating));
                    SelectedTabIndex = 1;
                    RefreshPlanView();
                    UpdateAllStatistics();
                    SaveDataToFile();

                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        var mainWindow = Application.Current.MainWindow as MainWindow;
                        if (mainWindow != null)
                        {
                            mainWindow.ShowRatingForAnime(anime);
                        }
                    }), DispatcherPriority.Normal);
                }
                else
                {
                    // Смена статуса (План ↔ Смотрю) влияет на счётчики и рекомендации — помечаем кэш устаревшим.
                    _isStatisticsDirty = true;
                    RefreshPlanView();

                    // Аниме сменило позицию в списке (например, «Смотрю» поднимается наверх) —
                    // даём окну возможность плавно показать его пользователю
                    if (anime != null && statusChanged)
                        PlanAnimeMoved?.Invoke(anime);
                }
            }
        }

        public static void ResetAnimeScores(Anime anime)
        {
            foreach (var part in anime.GetAllParts())
                part.ResetScores();
            anime.AdjustedScore = 0;
        }

        public ObservableCollection<PartViewModel> SelectedForRatingParts
        {
            get
            {
                var parts = new ObservableCollection<PartViewModel>();
                if (SelectedForRating == null)
                    return parts;

                int idx = 1;
                foreach (var s in SelectedForRating.Seasons)
                    parts.Add(new PartViewModel { Part = s, DisplayName = $"Сезон {idx++}" });
                idx = 1;
                foreach (var m in SelectedForRating.Movies)
                    parts.Add(new PartViewModel { Part = m, DisplayName = $"Фильм {idx++}" });
                // OVA и ONA не оцениваются (они только указываются в составе тайтла).
                // Исключение — тайтл без сезонов и фильмов: тогда оцениваются они (см. Anime.OvaAndOnaAreRated).
                if (SelectedForRating.OvaAndOnaAreRated)
                {
                    idx = 1;
                    foreach (var o in SelectedForRating.Ovas)
                        parts.Add(new PartViewModel { Part = o, DisplayName = $"OVA {idx++}" });
                    idx = 1;
                    foreach (var o in SelectedForRating.Onas)
                        parts.Add(new PartViewModel { Part = o, DisplayName = $"ONA {idx++}" });
                }

                return parts;
            }
        }

        private void CancelRating()
        {
            if (SelectedForRating == null) return;
            var anime = SelectedForRating;

            SelectedForRating = null;
            CurrentRatingAnime = null;
            OnPropertyChanged(nameof(SelectedForRating));
            OnPropertyChanged(nameof(CurrentRatingAnime));
            OnPropertyChanged(nameof(HasAnimeForRating));

            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                var mainWindow = Application.Current.MainWindow as MainWindow;
                if (mainWindow != null)
                {
                    mainWindow.ClearRatingTabControl();
                    mainWindow.RatingTab.IsEnabled = false;
                    mainWindow.RatingTab.GetBindingExpression(RadioButton.IsEnabledProperty)?.UpdateTarget();
                }
            }), DispatcherPriority.Background);

            if (_isReEvaluating)
            {
                TopList.Add(anime);
                SortTopList();
                _isReEvaluating = false;
                SelectedTabIndex = 2;
            }
            else
            {
                anime.Status = LocalizationManager.CurrentLanguage == "English" ? "Watching" : "Смотрю";
                if (!AnimeList.Contains(anime))
                {
                    AnimeList.Add(anime);
                }
                ResetAnimeScores(anime);
                RefreshPlanView();
                SelectedTabIndex = 0;
            }

            CommandManager.InvalidateRequerySuggested();

            RefreshPlanView();
            UpdateAllStatistics();
            SaveDataToFile();

            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                MainWindow.Instance?.SetActiveTabByIndex(SelectedTabIndex);
            }), DispatcherPriority.Normal);

            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                var mainWindow = Application.Current.MainWindow as MainWindow;
                if (mainWindow != null)
                {
                    var grid = mainWindow.FindName("PlanDataGrid") as DataGrid;
                    grid?.Items.Refresh();
                    grid?.UpdateLayout();
                }
            }), DispatcherPriority.Background);
        }

        private void AddAnime() { }

        /// <summary>Удаляет выбранное аниме из плана — после подтверждения (как и удаление из топа).</summary>
        private void DeleteAnime()
        {
            if (SelectedAnime == null) return;
            var anime = SelectedAnime;

            string message = string.Format(GetLocalizedString("Msg_DeletePlanAnimeConfirmation"), anime.Title);
            string title = GetLocalizedString("Msg_ConfirmDeleteTitle");
            var result = MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            AnimeList.Remove(anime);
            RefreshPlanView();
            UpdateAllStatistics();
            SaveDataToFile();
        }

        /// <summary>Выбирает случайное аниме из плана (без «Смотрю»/«Просмотрено») с учётом выбранного жанра.</summary>
        private void RandomAnime()
        {
            var source = AnimeList.Where(a =>
                a.Status != "Просмотрено" && a.Status != "Watched" &&
                a.Status != "Смотрю" && a.Status != "Watching");

            string allGenres = LocalizationManager.CurrentLanguage == "English" ? "All genres" : "Все жанры";
            if (SelectedRandomGenre != allGenres)
            {
                string selectedGenre = LocalizationManager.CurrentLanguage == "English"
                    ? GetRussianGenre(SelectedRandomGenre)
                    : SelectedRandomGenre;
                source = source.Where(a => a.Genre == selectedGenre || a.AdditionalGenres.Contains(selectedGenre));
            }

            var available = source.ToList();
            if (available.Count == 0)
            {
                RandomText = LocalizationManager.CurrentLanguage == "English" ? "No matching anime" : "Нет подходящих аниме";
                return;
            }

            var a = available[rand.Next(available.Count)];
            RandomText = a.Title;
        }

        public void ProcessWatchedAnime(Anime anime)
        {
            anime.ApplyTempScores();

            if (AnimeList.Contains(anime))
                AnimeList.Remove(anime);

            Application.Current.Dispatcher.Invoke(() =>
            {
                var mainWindow = Application.Current.MainWindow as MainWindow;
                if (mainWindow != null)
                {
                    mainWindow.ShowRatingForAnime(anime);
                }
            }, DispatcherPriority.Normal);

            RefreshPlanView();
            UpdateAllStatistics();
            CommandManager.InvalidateRequerySuggested();
        }

        public void ReloadData()
        {
            ClearAnimeList();
            TopList.Clear();
            LoadDataFromFile();
            UpdateAllStatistics();
            SelectedAnime = null;
            SelectedForRating = null;
            CurrentRatingAnime = null;
            SelectedTopAnime = null;
            SelectedTabIndex = 0;
        }

        /// <summary>Полностью очищает данные приложения: списки, кэш статистики, файл данных и резервные копии.</summary>
        public void ResetAllData()
        {
            _isResettingData = true;
            try
            {
                ClearAnimeList();
                TopList.Clear();
                SelectedAnime = null;
                SelectedForRating = null;
                CurrentRatingAnime = null;
                SelectedTopAnime = null;

                _isStatisticsDirty = true;
                _cachedTopGenres = null;
                _cachedAvgScoreByGenre = null;
                _cachedTopByYear = null;
                _cachedTopTen = null;
                _cachedFavoriteGenre = null;
                _cachedLongestAnime = null;
                _cachedShortestAnime = null;
                _cachedRecommendations = null;
                _cachedAvgEpisodesPerSeason = null;
                _cachedMedianEpisodesPerSeason = null;
                _cachedWatchedPercent = null;
                _cachedWatchingPercent = null;
                _cachedPlannedPercent = null;

                StatusSeries = new SeriesCollection();
                YearTrendSeries = new SeriesCollection();
                TopAnimeBarItems = new List<TopAnimeBarItem>();
                AvgScoreGenreItems = new List<GenreBarItem>();
                TopGenresBarItems = new List<GenreCountBarItem>();
                YearLabels = new List<string>();

                OnPropertyChanged(nameof(StatusSeries));
                OnPropertyChanged(nameof(YearTrendSeries));
                OnPropertyChanged(nameof(TopAnimeBarItems));
                OnPropertyChanged(nameof(AvgScoreGenreItems));
                OnPropertyChanged(nameof(TopGenresBarItems));
                OnPropertyChanged(nameof(YearLabels));

                if (File.Exists(DataFilePath))
                {
                    try { File.Delete(DataFilePath); }
                    catch (Exception ex) { Logger.Log($"Не удалось удалить файл данных при сбросе: {ex}"); }
                }

                if (File.Exists(DataFilePath))
                {
                    MessageBox.Show("Не удалось удалить файл данных. Возможно, он занят другим процессом. Попробуйте закрыть программу и удалить вручную.",
                                    "Ошибка сброса", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                try
                {
                    string backupFolder = Path.Combine(Path.GetDirectoryName(DataFilePath) ?? "", "Backups");
                    if (Directory.Exists(backupFolder))
                    {
                        foreach (var file in Directory.GetFiles(backupFolder, "*.json"))
                        {
                            try { File.Delete(file); } catch { }
                        }
                    }
                }
                catch { }

                SaveDataToFile();

                if (File.Exists(DataFilePath))
                {
                    string content = File.ReadAllText(DataFilePath);
                    var data = JsonSerializer.Deserialize<SaveData>(content);
                    if (data == null || data.AnimeList.Count > 0 || data.TopList.Count > 0)
                    {
                        Logger.Log("ОШИБКА: после сброса данные не пустые!");
                        MessageBox.Show("Не удалось полностью очистить данные. Проверьте права доступа к файлу.",
                                        "Ошибка сброса", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    Logger.Log("ОШИБКА: файл данных не создан после сброса!");
                }

                RefreshPlanView();
                UpdateAllStatistics();
                UpdateCharts();

                OnPropertyChanged(nameof(TopByYear));
                OnPropertyChanged(nameof(AvgScoreByGenre));
                OnPropertyChanged(nameof(AvgEpisodesPerSeason));
                OnPropertyChanged(nameof(MedianEpisodesPerSeason));
                OnPropertyChanged(nameof(Recommendations));
                OnPropertyChanged(nameof(WatchedPercent));
                OnPropertyChanged(nameof(WatchingPercent));
                OnPropertyChanged(nameof(PlannedPercent));

                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    var mainWindow = Application.Current.MainWindow as MainWindow;
                    if (mainWindow != null)
                    {
                        mainWindow.RatingTab.IsEnabled = false;
                        mainWindow.RatingTab.GetBindingExpression(RadioButton.IsEnabledProperty)?.UpdateTarget();
                    }
                }), DispatcherPriority.Background);
            }
            finally
            {
                _isResettingData = false;
            }
        }

        public void AutoResolveDuplicateAdjustedScores()
        {
            var groups = TopList.GroupBy(a => Math.Round(a.AdjustedScore, 3))
                                .Where(g => g.Count() > 1);
            foreach (var group in groups)
            {
                var list = group.OrderByDescending(a => a.AdjustedScore).ToList();
                for (int i = 1; i < list.Count; i++)
                {
                    list[i].AdjustedScore = list[i - 1].AdjustedScore - 0.01;
                }
            }
            SortTopList();
        }

        private ObservableCollection<Anime>? _filteredTopList;
        public ObservableCollection<Anime> FilteredTopList
        {
            get => _filteredTopList ?? TopList;
            set { _filteredTopList = value; OnPropertyChanged(); }
        }

        private void ApplyTopFilter()
        {
            if (string.IsNullOrWhiteSpace(SearchTextTop))
            {
                FilteredTopList = TopList;
            }
            else
            {
                var filtered = TopList.Where(a =>
                    a.Title.Contains(SearchTextTop, StringComparison.OrdinalIgnoreCase)).ToList();
                FilteredTopList = new ObservableCollection<Anime>(filtered);
            }
        }

        /// <summary>
        /// Добавляет оценённое аниме в топ: вычисляет итоговый балл, при совпадении баллов запускает
        /// диалог сравнения («что лучше?») и гарантирует уникальность AdjustedScore.
        /// </summary>
        private void AddToTop()
        {
            if (SelectedForRating == null) return;
            var anime = SelectedForRating;

            double baseScore = anime.Score;
            if (baseScore <= 0) baseScore = 1.0;

            anime.AdjustedScore = baseScore;
            TopList.Add(anime);
            SortTopList();

            var sameScoreGroup = TopList.Where(a => Math.Abs(a.Score - baseScore) < 0.0001)
                                        .OrderByDescending(a => a.AdjustedScore)
                                        .ToList();
            if (sameScoreGroup.Count > 1)
            {
                for (int i = 1; i < sameScoreGroup.Count; i++)
                {
                    var higher = sameScoreGroup[i - 1];
                    var lower = sameScoreGroup[i];
                    var dialog = new ConflictResolutionWindow(higher, lower) { Owner = Application.Current.MainWindow };
                    if (dialog.ShowDialog() == true)
                    {
                        var winner = dialog.SelectedWinner == 2 ? lower : higher;
                        var loser = dialog.SelectedWinner == 2 ? higher : lower;
                        double topValue = Math.Max(higher.AdjustedScore, lower.AdjustedScore);
                        winner.AdjustedScore = topValue;
                        loser.AdjustedScore = topValue - 0.0001;

                        SortTopList();
                        sameScoreGroup = TopList.Where(a => Math.Abs(a.Score - baseScore) < 0.0001)
                                                .OrderByDescending(a => a.AdjustedScore)
                                                .ToList();
                    }
                    else
                    {
                        TopList.Remove(anime);
                        SortTopList();
                        SelectedForRating = anime;
                        OnPropertyChanged(nameof(SelectedForRating));
                        return;
                    }
                }
                EnsureUniqueAdjustedScores(sameScoreGroup);
            }
            else
            {
                var existingScores = TopList.Select(a => a.AdjustedScore).ToList();
                double newAdjusted = baseScore;
                while (existingScores.Any(x => Math.Abs(x - newAdjusted) < 0.00001)) newAdjusted += 0.0001;
                anime.AdjustedScore = newAdjusted;
                SortTopList();
            }

            SelectedForRating = null;
            CurrentRatingAnime = null;
            OnPropertyChanged(nameof(SelectedForRating));
            OnPropertyChanged(nameof(CurrentRatingAnime));
            OnPropertyChanged(nameof(HasAnimeForRating));

            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                var mainWindow = Application.Current.MainWindow as MainWindow;
                if (mainWindow != null)
                {
                    mainWindow.RatingTab.IsEnabled = false;
                    mainWindow.RatingTab.GetBindingExpression(RadioButton.IsEnabledProperty)?.UpdateTarget();
                }
            }), DispatcherPriority.Background);

            SelectedTabIndex = 2;
            OnPropertyChanged(nameof(SelectedTabIndex));

            _isReEvaluating = false;
            UpdateAllStatistics();
            SaveDataToFile();

            CommandManager.InvalidateRequerySuggested();

            // Диаграммы уже обновлены внутри UpdateAllStatistics(); повторное обновление откладываем
            // на момент, когда интерфейс успеет переключиться на вкладку «Топ».
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                UpdateCharts();
            }), DispatcherPriority.Background);

            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                MainWindow.Instance?.SetActiveTabByIndex(2);
            }));
        }

        private void EnsureUniqueAdjustedScores(List<Anime> group)
        {
            var sorted = group.OrderByDescending(a => a.AdjustedScore).ToList();
            for (int i = 1; i < sorted.Count; i++)
            {
                if (Math.Abs(sorted[i].AdjustedScore - sorted[i - 1].AdjustedScore) < 0.00001)
                {
                    sorted[i].AdjustedScore = sorted[i - 1].AdjustedScore - 0.0001;
                }
            }
            SortTopList();
        }

        public void MigrateTopListScores()
        {
            foreach (var a in TopList) a.AdjustedScore = a.Score;
            var groups = TopList.GroupBy(a => Math.Round(a.Score, 4)).Where(g => g.Count() > 1);
            foreach (var group in groups)
            {
                var list = group.OrderByDescending(a => a.Score).ThenBy(a => a.Rank).ToList();
                for (int i = 0; i < list.Count; i++)
                    list[i].AdjustedScore = list[i].Score + (list.Count - i) * 0.0001;
            }
            SortTopList();
            SaveDataToFile();
        }

        /// <summary>Сортирует топ по убыванию итогового балла, обновляет места (Rank) и сбрасывает кэш статистики.</summary>
        public void SortTopList()
        {
            var sorted = TopList.OrderByDescending(a => a.AdjustedScore).ToList();
            ReplaceItems(TopList, sorted);
            UpdateRanks();
            _isStatisticsDirty = true;
        }

        private void UpdateRanks()
        {
            for (int i = 0; i < TopList.Count; i++) TopList[i].Rank = i + 1;
        }

        private void ReEvaluate()
        {
            if (SelectedTopAnime == null) return;
            var anime = SelectedTopAnime;
            _isReEvaluating = true;
            TopList.Remove(anime);
            anime.AdjustedScore = anime.Score;
            SelectedForRating = anime;
            CurrentRatingAnime = anime;
            OnPropertyChanged(nameof(SelectedForRatingParts));
            OnPropertyChanged(nameof(HasAnimeForRating));
            OnPropertyChanged(nameof(CurrentRatingAnime));
            SelectedTabIndex = 1;
            SortTopList();
            UpdateAllStatistics();
            SaveDataToFile();
            OnPropertyChanged(nameof(SelectedForRating));

            CommandManager.InvalidateRequerySuggested();

            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                var mainWindow = Application.Current.MainWindow as MainWindow;
                if (mainWindow != null)
                {
                    mainWindow.ShowRatingForAnime(anime);
                }
            }), DispatcherPriority.Normal);
        }

        private void EditTopAnime()
        {
            if (SelectedTopAnime == null) return;
            var editWindow = new Views.EditAnimeWindow(SelectedTopAnime) { Owner = Application.Current.MainWindow };
            if (editWindow.ShowDialog() == true)
            {
                SortTopList();
                RefreshPlanView();
                UpdateAllStatistics();
                SaveDataToFile();
            }
        }

        private void EditPlanAnime()
        {
            if (SelectedAnime == null) return;
            var anime = SelectedAnime;
            var editWindow = new Views.EditAnimeWindow(anime) { Owner = Application.Current.MainWindow };
            if (editWindow.ShowDialog() == true)
            {
                RefreshPlanView();
                UpdateAllStatistics();
                SaveDataToFile();

                // Название могло измениться, а значит и место в списке (сортировка по названию) — показываем аниме
                NotifyPlanAnimeMoved(anime);
            }
        }

        private void DeleteFromTop()
        {
            if (SelectedTopAnime == null) return;
            var anime = SelectedTopAnime;

            string message = string.Format(GetLocalizedString("Msg_DeleteAnimeConfirmation"), anime.Title);
            string title = GetLocalizedString("Msg_ConfirmDeleteTitle");
            var result = MessageBox.Show(
                message,
                title,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            TopList.Remove(anime);
            SortTopList();
            RefreshPlanView();
            UpdateAllStatistics();
            SaveDataToFile();
        }

        // ========== МЕТОДЫ СТАТИСТИКИ ==========

        /// <summary>Перечисляет все непустые жанры аниме: сначала основной, затем дополнительные.</summary>
        private static IEnumerable<string> EnumerateGenres(Anime anime)
        {
            if (!string.IsNullOrEmpty(anime.Genre))
                yield return anime.Genre;

            if (anime.AdditionalGenres != null)
            {
                foreach (var genre in anime.AdditionalGenres)
                    if (!string.IsNullOrEmpty(genre))
                        yield return genre;
            }
        }

        private List<GenreStat> ComputeTopGenres()
        {
            var allGenres = TopList.SelectMany(EnumerateGenres);
            var groups = allGenres.GroupBy(g => g)
                                  .Select(g => new { Genre = g.Key, Count = g.Count() })
                                  .OrderByDescending(g => g.Count)
                                  .Take(10)
                                  .ToList();
            int max = groups.FirstOrDefault()?.Count ?? 1;
            return groups.Select(g => new GenreStat
            {
                Genre = g.Genre,
                Count = g.Count,
                MaxCount = max
            }).ToList();
        }

        /// <summary>
        /// Средний итоговый балл по каждому жанру (топ-10 по убыванию).
        /// Считается за ОДИН проход по топу (раньше — отдельный проход по всему списку на каждый жанр).
        /// Порядок жанров при равных баллах сохраняется: по первому появлению в списке.
        /// </summary>
        private List<GenreStat> ComputeAvgScoreByGenre()
        {
            var order = new List<string>();                       // жанры в порядке первого появления
            var sums = new Dictionary<string, (double Sum, int Count)>();

            foreach (var anime in TopList)
            {
                // Distinct: аниме учитывается в жанре один раз, даже если жанр указан и как основной, и как дополнительный
                foreach (var genre in EnumerateGenres(anime).Distinct())
                {
                    if (sums.TryGetValue(genre, out var acc))
                    {
                        sums[genre] = (acc.Sum + anime.AdjustedScore, acc.Count + 1);
                    }
                    else
                    {
                        order.Add(genre);
                        sums[genre] = (anime.AdjustedScore, 1);
                    }
                }
            }

            return order
                .Select(g => new GenreStat { Genre = g, Score = sums[g].Sum / sums[g].Count })
                .OrderByDescending(g => g.Score)
                .Take(10)
                .ToList();
        }

        private List<YearTopEntry> ComputeTopByYear()
        {
            return TopList.GroupBy(a => a.YearsDisplay?.Split(',').FirstOrDefault()?.Trim() ?? "—")
                          .Select(g => new YearTopEntry
                          {
                              Year = g.Key,
                              TopTitle = g.OrderByDescending(a => a.AdjustedScore).FirstOrDefault()?.Title ?? "—",
                              TopScore = g.Max(a => a.AdjustedScore)
                          })
                          .OrderByDescending(y => y.TopScore)
                          .Take(10)
                          .ToList();
        }

        private string ComputeFavoriteGenre()
        {
            var allGenres = TopList.SelectMany(EnumerateGenres);
            var favorite = allGenres.GroupBy(g => g)
                                    .OrderByDescending(g => g.Count())
                                    .FirstOrDefault()?.Key;
            return favorite ?? "—";
        }

        private string ComputeLongestAnime()
        {
            var max = TopList.OrderByDescending(a => a.TotalEpisodeCount).FirstOrDefault();
            return max != null ? $"{max.Title} ({max.TotalEpisodeCount} эп.)" : "—";
        }

        private string ComputeShortestAnime()
        {
            var min = TopList.Where(a => a.TotalEpisodeCount > 0).OrderBy(a => a.TotalEpisodeCount).FirstOrDefault();
            return min != null ? $"{min.Title} ({min.TotalEpisodeCount} эп.)" : "—";
        }

        private List<string> ComputeRecommendations()
        {
            var favGenres = TopGenres.Select(g => g.Genre).ToList();
            if (!favGenres.Any())
            {
                string msg = LocalizationManager.CurrentLanguage == "English"
                    ? "Not enough data for recommendations."
                    : "Пока недостаточно данных для рекомендаций.";
                return new List<string> { msg };
            }

            var candidates = AnimeList
                .Where(a => a.Status != "Просмотрено" && a.Status != "Watched")
                .Where(a => a.AdditionalGenres != null && a.AdditionalGenres.Any(g => favGenres.Contains(g)))
                .Take(5)
                .Select(a => a.Title)
                .ToList();

            if (candidates.Any())
                return candidates;
            else
            {
                string msg = LocalizationManager.CurrentLanguage == "English"
                    ? "Add more anime to the plan to get recommendations."
                    : "Добавьте больше аниме в план, чтобы получить рекомендации.";
                return new List<string> { msg };
            }
        }

        private double ComputeAvgEpisodesPerSeason()
        {
            var seasons = TopList.SelectMany(a => a.Seasons).ToList();
            if (!seasons.Any()) return 0;
            return seasons.Average(s => s.EpisodeCount);
        }

        private double ComputeMedianEpisodesPerSeason()
        {
            var episodes = TopList.SelectMany(a => a.Seasons).Select(s => s.EpisodeCount).OrderBy(e => e).ToList();
            if (!episodes.Any()) return 0;
            int mid = episodes.Count / 2;
            return episodes.Count % 2 == 0 ? (episodes[mid - 1] + episodes[mid]) / 2.0 : episodes[mid];
        }

        private int ComputeWatchedPercent()
        {
            int total = WatchedCount + PlannedCount + WatchingCount;
            return total == 0 ? 0 : (int)Math.Round((double)WatchedCount / total * 100);
        }

        private int ComputeWatchingPercent()
        {
            int total = WatchedCount + PlannedCount + WatchingCount;
            return total == 0 ? 0 : (int)Math.Round((double)WatchingCount / total * 100);
        }

        private int ComputePlannedPercent()
        {
            int total = WatchedCount + PlannedCount + WatchingCount;
            return total == 0 ? 0 : (int)Math.Round((double)PlannedCount / total * 100);
        }

        /// <summary>
        /// Помечает статистику устаревшей и уведомляет интерфейс. Сами значения пересчитываются лениво —
        /// при первом обращении (см. <see cref="EnsureStatisticsFresh"/>).
        /// </summary>
        public void UpdateAllStatistics()
        {
            _isStatisticsDirty = true;
            OnPropertyChanged(nameof(WatchedCount));
            OnPropertyChanged(nameof(PlannedCount));
            OnPropertyChanged(nameof(TotalEpisodes));
            OnPropertyChanged(nameof(TotalTimeString));
            OnPropertyChanged(nameof(TopGenres));
            OnPropertyChanged(nameof(TopTen));
            OnPropertyChanged(nameof(FavoriteGenre));
            OnPropertyChanged(nameof(LongestAnime));
            OnPropertyChanged(nameof(ShortestAnime));
            OnPropertyChanged(nameof(AvgScoreByGenre));
            OnPropertyChanged(nameof(TopByYear));
            OnPropertyChanged(nameof(WatchingCount));
            OnPropertyChanged(nameof(WatchedPercent));
            OnPropertyChanged(nameof(WatchingPercent));
            OnPropertyChanged(nameof(PlannedPercent));
            OnPropertyChanged(nameof(AvgEpisodesPerSeason));
            OnPropertyChanged(nameof(MedianEpisodesPerSeason));
            OnPropertyChanged(nameof(Recommendations));

            UpdateCharts();
        }

        /// <summary>Акцентный цвет текущей темы (ресурс AccentBrush); запасной вариант — фиолетовый цвет темы по умолчанию.</summary>
        private static Color GetThemeAccentColor()
        {
            if (Application.Current?.TryFindResource("AccentBrush") is SolidColorBrush brush)
                return brush.Color;
            return Color.FromRgb(0x7F, 0x6A, 0xFF);
        }

        /// <summary>Пересобирает данные всех диаграмм и «полосовых» списков вкладки «Статистика».</summary>
        public void UpdateCharts()
        {
            // Круговая диаграмма статусов
            StatusSeries.Clear();

            string watchedLabel = GetLocalizedString("Stats_Watched");
            string watchingLabel = GetLocalizedString("Stats_WatchingPercent");
            string plannedLabel = GetLocalizedString("Stats_PlannedPercent");

            StatusSeries.Add(new PieSeries
            {
                Title = watchedLabel,
                Values = new ChartValues<double> { WatchedCount },
                Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6BCB77")),
                PushOut = 8,
                DataLabels = true,
                LabelPoint = point => $"{point.Participation:P0}",
                Foreground = new SolidColorBrush(Colors.White)
            });
            StatusSeries.Add(new PieSeries
            {
                Title = watchingLabel,
                Values = new ChartValues<double> { WatchingCount },
                Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFD93D")),
                PushOut = 8,
                DataLabels = true,
                LabelPoint = point => $"{point.Participation:P0}",
                Foreground = new SolidColorBrush(Colors.Black)
            });
            StatusSeries.Add(new PieSeries
            {
                Title = plannedLabel,
                Values = new ChartValues<double> { PlannedCount },
                Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6B6B")),
                PushOut = 8,
                DataLabels = true,
                LabelPoint = point => $"{point.Participation:P0}",
                Foreground = new SolidColorBrush(Colors.White)
            });

            // Топ-10 аниме — больше не через LiveCharts (после долгой борьбы с обрезкой
            // и поворотом подписей). Название рисуется обычным TextBlock над полосой —
            // текст ничем не ограничен и переносится на несколько строк при необходимости,
            // поэтому обрезка здесь в принципе невозможна.
            var topTen = TopList.Take(10).ToList();
            var topAnimeBarItems = new List<TopAnimeBarItem>();
            foreach (var a in topTen)
            {
                double score = a.AdjustedScore > 0 ? a.AdjustedScore : a.Score;
                topAnimeBarItems.Add(new TopAnimeBarItem { Title = a.Title, Score = score });
            }
            TopAnimeBarItems = topAnimeBarItems;

            // Топ-5 жанров — тот же нативный дизайн, что у двух других карточек,
            // но горизонтально (без LiveCharts). Пропорция полосы считается
            // относительно MaxCount — наибольшего количества среди этих пяти
            // жанров (в отличие от баллов 0..10 тут нет фиксированной шкалы).
            var topGenres = TopGenres.Take(5).ToList();
            bool isEnglish = LocalizationManager.CurrentLanguage == "English";
            int maxGenreCount = topGenres.Count > 0 ? topGenres.Max(g => g.Count) : 1;
            var topGenresBarItems = topGenres.Select(g => new GenreCountBarItem
            {
                Title = isEnglish ? GetEnglishGenre(g.Genre) : g.Genre,
                Count = g.Count,
                MaxCount = maxGenreCount
            }).ToList();
            TopGenresBarItems = topGenresBarItems;

            // Средний балл по жанрам — тот же дизайн, что "Топ-10 аниме" (название
            // сверху, полоса снизу, без LiveCharts). Строится НАПРЯМУЮ из
            // AvgScoreByGenre — это уже готовый список (все жанры, до 10 штук,
            // отсортированные по убыванию балла). Раньше список для этой карточки
            // собирался через пересечение с topGenres (который отсортирован по
            // КОЛИЧЕСТВУ, не по баллу) — из-за этого и терялся порядок по убыванию,
            // и обрезалось до 5 жанров вместо всех имеющихся.
            var avgScoreItems = new List<GenreBarItem>();
            foreach (var g in AvgScoreByGenre)
            {
                avgScoreItems.Add(new GenreBarItem
                {
                    Title = isEnglish ? GetEnglishGenre(g.Genre) : g.Genre,
                    Score = g.Score
                });
            }
            AvgScoreGenreItems = avgScoreItems;

            // Линейный график по годам
            // Цвет линии берём из текущей темы (раньше был зашит фиолетовый и не менялся при смене темы)
            Color accentColor = GetThemeAccentColor();
            var yearData = TopByYear.Take(10).ToList();
            if (yearData.Any() && yearData.Count > 1)
            {
                YearLabels = yearData.Select(y => y.Year).ToList();
                var yearScores = yearData.Select(y => y.TopScore).ToList();
                YearTrendSeries = new SeriesCollection
                {
                    new LineSeries
                    {
                        Title = "Топ-балл",
                        Values = new ChartValues<double>(yearScores),
                        Fill = new SolidColorBrush(Color.FromArgb(0x30, accentColor.R, accentColor.G, accentColor.B)),
                        Stroke = new SolidColorBrush(accentColor),
                        PointGeometry = DefaultGeometries.Circle,
                        PointGeometrySize = 12,
                        DataLabels = true,
                        LabelPoint = point => $"{point.Y:F2}",
                        Foreground = Brushes.White,
                        LineSmoothness = 0.5,
                        StrokeThickness = 3
                    }
                };
            }
            else
            {
                YearLabels = new List<string>();
                YearTrendSeries = new SeriesCollection();
            }

            OnPropertyChanged(nameof(StatusSeries));
            OnPropertyChanged(nameof(YearTrendSeries));
            OnPropertyChanged(nameof(TopAnimeBarItems));
            OnPropertyChanged(nameof(AvgScoreGenreItems));
            OnPropertyChanged(nameof(TopGenresBarItems));
            OnPropertyChanged(nameof(YearLabels));

            // Принудительное обновление UI
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                var mainWindow = Application.Current.MainWindow as MainWindow;
                if (mainWindow != null)
                {
                    var statsPage = mainWindow.FindName("StatsPage") as FrameworkElement;
                    statsPage?.UpdateLayout();
                }
            }), DispatcherPriority.Background);
        }

        // --- Загрузка/сохранение ---
        private static readonly string DataFileName = "animeData.json";
        private static readonly string DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AnimeTracker");
        public static readonly string DataFilePath = Path.Combine(DataDirectory, DataFileName);

        /// <summary>Сериализует оба списка в JSON и создаёт резервную копию при необходимости.</summary>
        public void SaveDataToFile()
        {
            try
            {
                if (!Directory.Exists(DataDirectory))
                    Directory.CreateDirectory(DataDirectory);

                SaveData data = new()
                {
                    AnimeList = AnimeList.ToList(),
                    TopList = TopList.ToList()
                };
                string json = JsonSerializer.Serialize(data);

                // Атомарная запись: сначала во временный файл, затем подмена. Если приложение завершится
                // посреди записи, основной файл с данными останется целым (а не обрезанным).
                string tempPath = DataFilePath + ".tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, DataFilePath, overwrite: true);

                BackupManager.CreateBackupIfNeeded();
            }
            catch (Exception ex)
            {
                Logger.Log($"SaveDataToFile error: {ex}");
            }
        }

        /// <summary>
        /// Читает данные из JSON, выполняет миграцию старых форматов и заполняет списки.
        /// Во время загрузки уведомления о смене коллекций подавляются (_isLoadingData).
        /// </summary>
        public void LoadDataFromFile()
        {
            bool dataLoaded = false;

            lock (_loadLock)
            {
                if (_isLoadingData) return;
                _isLoadingData = true;
                try
                {
                    if (!File.Exists(DataFilePath))
                        return;

                    string json = File.ReadAllText(DataFilePath);
                    var data = JsonSerializer.Deserialize<SaveData>(json);
                    if (data != null)
                    {
                        // Сначала мигрируем данные, затем подменяем коллекции одной операцией (один Reset вместо сотен Add).
                        foreach (var a in data.AnimeList) MigrateAnimeParts(a);
                        foreach (var a in data.TopList) MigrateAnimeParts(a);
                        ReplaceItems(AnimeList, data.AnimeList);
                        ReplaceItems(TopList, data.TopList);

                        // ИСПРАВЛЕНИЕ: во время загрузки обработчик CollectionChanged подавлен (_isLoadingData),
                        // поэтому загруженные аниме раньше НЕ подписывались на смену статуса — и если сменить статус
                        // прямо в таблице (например, на «Просмотрено»), аниме не переходило на вкладку «Оценка»
                        // до перезапуска приложения. Подписываем их явно. Топ не подписываем: оценённые аниме
                        // не должны уходить на повторную оценку при редактировании.
                        foreach (var loaded in AnimeList)
                            SubscribeToAnime(loaded);

                        foreach (var a in TopList)
                        {
                            a.RaisePropertyChanged(nameof(a.Score));
                            if (a.AdjustedScore == 0)
                                a.AdjustedScore = a.Score;
                        }

                        SortTopList();

                        _isStatisticsDirty = true;
                        dataLoaded = true;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"LoadDataFromFile error: {ex}");
                }
                finally
                {
                    _isLoadingData = false;
                }
            }

            if (dataLoaded)
                RefreshPlanView();
        }

        /// <summary>Приводит части аниме (сезоны, фильмы, OVA) к актуальному формату данных: выравнивает списки оценок опенингов/эндингов и исправляет нулевые временные оценки.</summary>
        private void MigrateAnimeParts(Anime anime)
        {
            foreach (var s in anime.Seasons)
            {
                s.AdjustOpeningScoresList();
                s.AdjustEndingScoresList();

                if (s.TempStory == 0) s.TempStory = 1;
                if (s.TempMainCharacters == 0) s.TempMainCharacters = 1;
                if (s.TempCharacters == 0) s.TempCharacters = 1;
                if (s.TempVisual == 0) s.TempVisual = 1;
                if (s.TempSound == 0) s.TempSound = 1;
                if (s.TempEmotions == 0) s.TempEmotions = 1;
                if (s.TempOriginality == 0) s.TempOriginality = 1;
                if (s.TempFinale == 0) s.TempFinale = 1;
            }
            foreach (var m in anime.Movies)
            {
                m.AdjustOpeningScoresList();
                m.AdjustEndingScoresList();

                if (m.TempStory == 0) m.TempStory = 1;
                if (m.TempMainCharacters == 0) m.TempMainCharacters = 1;
                if (m.TempCharacters == 0) m.TempCharacters = 1;
                if (m.TempVisual == 0) m.TempVisual = 1;
                if (m.TempSound == 0) m.TempSound = 1;
                if (m.TempEmotions == 0) m.TempEmotions = 1;
                if (m.TempOriginality == 0) m.TempOriginality = 1;
                if (m.TempFinale == 0) m.TempFinale = 1;
            }
            foreach (var o in anime.Ovas)
            {
                o.AdjustOpeningScoresList();
                o.AdjustEndingScoresList();

                if (o.TempStory == 0) o.TempStory = 1;
                if (o.TempMainCharacters == 0) o.TempMainCharacters = 1;
                if (o.TempCharacters == 0) o.TempCharacters = 1;
                if (o.TempVisual == 0) o.TempVisual = 1;
                if (o.TempSound == 0) o.TempSound = 1;
                if (o.TempEmotions == 0) o.TempEmotions = 1;
                if (o.TempOriginality == 0) o.TempOriginality = 1;
                if (o.TempFinale == 0) o.TempFinale = 1;
            }
            foreach (var o in anime.Onas)
            {
                o.AdjustOpeningScoresList();
                o.AdjustEndingScoresList();

                if (o.TempStory == 0) o.TempStory = 1;
                if (o.TempMainCharacters == 0) o.TempMainCharacters = 1;
                if (o.TempCharacters == 0) o.TempCharacters = 1;
                if (o.TempVisual == 0) o.TempVisual = 1;
                if (o.TempSound == 0) o.TempSound = 1;
                if (o.TempEmotions == 0) o.TempEmotions = 1;
                if (o.TempOriginality == 0) o.TempOriginality = 1;
                if (o.TempFinale == 0) o.TempFinale = 1;
            }
        }

        private string LoadEulaText()
        {
            string eulaFileName = LocalizationManager.CurrentLanguage == "English" ? "LICENSE_EN.txt" : "LICENSE_RU.txt";
            string eulaPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, eulaFileName);
            if (File.Exists(eulaPath))
                return File.ReadAllText(eulaPath);
            return LocalizationManager.CurrentLanguage == "English" ? "License agreement not found." : "Лицензионное соглашение не найдено.";
        }

        /// <summary>Приводит коллекцию к желаемому списку минимальным числом вставок/удалений — выпадающий список не «мигает» и не теряет выбор.</summary>
        private static void SyncCollection(ObservableCollection<string> target, List<string> desired)
        {
            for (int i = target.Count - 1; i >= 0; i--)
                if (!desired.Contains(target[i]))
                    target.RemoveAt(i);

            for (int i = 0; i < desired.Count; i++)
                if (i >= target.Count || target[i] != desired[i])
                    target.Insert(i, desired[i]);
        }

        private void UpdateRandomGenreOptions()
        {
            bool isEnglish = LocalizationManager.CurrentLanguage == "English";
            string allGenres = isEnglish ? "All genres" : "Все жанры";
            var genres = new[] { allGenres }.Concat(_genreOptions.Select(g => isEnglish ? GetEnglishGenre(g) : g).OrderBy(g => g)).ToList();
            SyncCollection(_randomGenreOptions, genres);
        }

        // ───────────── Локализация жанров и статусов ─────────────
        // В данных жанры хранятся на русском; английские названия используются только для отображения.
        public static string GetEnglishGenre(string russianGenre) => russianGenre switch
        {
            "Экшен" => "Action",
            "Сёнэн" => "Shonen",
            "Фэнтези" => "Fantasy",
            "Исекай" => "Isekai",
            "Комедия" => "Comedy",
            "Романтика" => "Romance",
            "Школа" => "School",
            "Приключения" => "Adventure",
            "Повседневность" => "Slice of Life",
            "Драма" => "Drama",
            "Сэйнэн" => "Seinen",
            "Сёдзё" => "Shojo",
            "Фантастика" => "Sci-Fi",
            "Детектив" => "Detective",
            "Психологический триллер" => "Psychological Thriller",
            "Боевые искусства" => "Martial Arts",
            "Психология" => "Psychological",
            "Гарем" => "Harem",
            "Спокон" => "Sports",
            "Дзёсэй" => "Josei",
            "Исторический" => "Historical",
            "Пост-апокалипсис" => "Post-Apocalyptic",
            "Музыка" => "Music",
            "Меха" => "Mecha",
            "Киберпанк" => "Cyberpunk",
            "Кодомо" => "Kodomo",
            "Махо-сёдзё" => "Mahou Shoujo",
            "Стимпанк" => "Steampunk",
            "Спорт" => "Sport",
            "Вампиры" => "Vampire",
            "Демоны" => "Demons",
            "Военное" => "Military",
            "Космос" => "Space",
            "Магия" => "Magic",
            "Пародия" => "Parody",
            "Полиция" => "Police",
            "Самураи" => "Samurai",
            "Трагедия" => "Tragedy",
            "Триллер" => "Thriller",
            "Этти" => "Ecchi",
            "Юри" => "Yuri",
            "Яой" => "Yaoi",
            "Гурман" => "Gourmet",
            "Гонки" => "Racing",
            "Мафия" => "Mafia",
            "Медицина" => "Medical",
            "Мистика" => "Mystery",
            "Ужасы" => "Horror",
            "Сверхъестественное" => "Supernatural",
            "Прокси бои" => "Proxy Battles",
            "Супер способности" => "Superpowers",
            "Нелинейный Сюжет" => "Nonlinear Plot",
            "Бисёнен" => "Bishounen",
            "Любовный треугольник" => "Love Triangle",
            "Игры" => "Games",
            "Эльфы" => "Elves",
            "Антиутопия" => "Dystopia",
            "Лоликон" => "Lolicon",
            "Сёдзё-ай" => "Shoujo Ai",
            "Божества" => "Deities",
            "Эротика" => "Erotica",
            "Воры" => "Thieves",
            "Драконы" => "Dragons",
            "Параллельный мир" => "Parallel World",
            "Сражения на мечах" => "Sword Fights",
            "Инопланетяне" => "Aliens",
            "Ангелы" => "Angels",
            "Безумие" => "Madness",
            "Виртуальная реальность" => "Virtual Reality",
            "Убийцы" => "Assassins",
            "Ниндзя" => "Ninja",
            "Тёмное Фэнтези" => "Dark Fantasy",
            "Перестрелки" => "Gunfights",
            _ => russianGenre
        };

        public static string GetRussianGenre(string englishGenre) => englishGenre switch
        {
            "Action" => "Экшен",
            "Shonen" => "Сёнэн",
            "Fantasy" => "Фэнтези",
            "Isekai" => "Исекай",
            "Comedy" => "Комедия",
            "Romance" => "Романтика",
            "School" => "Школа",
            "Adventure" => "Приключения",
            "Slice of Life" => "Повседневность",
            "Drama" => "Драма",
            "Seinen" => "Сэйнэн",
            "Shojo" => "Сёдзё",
            "Sci-Fi" => "Фантастика",
            "Detective" => "Детектив",
            "Psychological Thriller" => "Психологический триллер",
            "Martial Arts" => "Боевые искусства",
            "Psychological" => "Психология",
            "Harem" => "Гарем",
            "Sports" => "Спокон",
            "Josei" => "Дзёсэй",
            "Historical" => "Исторический",
            "Post-Apocalyptic" => "Пост-апокалипсис",
            "Music" => "Музыка",
            "Mecha" => "Меха",
            "Cyberpunk" => "Киберпанк",
            "Kodomo" => "Кодомо",
            "Mahou Shoujo" => "Махо-сёдзё",
            "Steampunk" => "Стимпанк",
            "Sport" => "Спорт",
            "Vampire" => "Вампиры",
            "Demons" => "Демоны",
            "Military" => "Военное",
            "Space" => "Космос",
            "Magic" => "Магия",
            "Parody" => "Пародия",
            "Police" => "Полиция",
            "Samurai" => "Самураи",
            "Tragedy" => "Трагедия",
            "Thriller" => "Триллер",
            "Ecchi" => "Этти",
            "Yuri" => "Юри",
            "Yaoi" => "Яой",
            "Gourmet" => "Гурман",
            "Racing" => "Гонки",
            "Mafia" => "Мафия",
            "Medical" => "Медицина",
            "Mystery" => "Мистика",
            "Horror" => "Ужасы",
            "Supernatural" => "Сверхъестественное",
            "Proxy Battles" => "Прокси бои",
            "Superpowers" => "Супер способности",
            "Nonlinear Plot" => "Нелинейный Сюжет",
            "Bishounen" => "Бисёнен",
            "Love Triangle" => "Любовный треугольник",
            "Games" => "Игры",
            "Elves" => "Эльфы",
            "Dystopia" => "Антиутопия",
            "Lolicon" => "Лоликон",
            "Shoujo Ai" => "Сёдзё-ай",
            "Deities" => "Божества",
            "Erotica" => "Эротика",
            "Thieves" => "Воры",
            "Dragons" => "Драконы",
            "Parallel World" => "Параллельный мир",
            "Sword Fights" => "Сражения на мечах",
            "Aliens" => "Инопланетяне",
            "Angels" => "Ангелы",
            "Madness" => "Безумие",
            "Virtual Reality" => "Виртуальная реальность",
            "Assassins" => "Убийцы",
            "Ninja" => "Ниндзя",
            "Dark Fantasy" => "Тёмное Фэнтези",
            "Gunfights" => "Перестрелки",
            _ => englishGenre
        };

        public static string GetEnglishStatus(string russianStatus) => russianStatus switch
        {
            "План" => "Plan",
            "Смотрю" => "Watching",
            "Просмотрено" => "Watched",
            _ => russianStatus
        };

        public static string GetRussianStatus(string englishStatus) => englishStatus switch
        {
            "Plan" => "План",
            "Watching" => "Смотрю",
            "Watched" => "Просмотрено",
            _ => englishStatus
        };

        public static string GetLocalizedString(string key)
        {
            return (string)Application.Current.FindResource(key);
        }

        public void MoveToRatingIfWatched(Anime anime)
        {
            if (anime.Status == "Просмотрено" || anime.Status == "Watched")
            {
                anime.ApplyTempScores();
                AnimeList.Remove(anime);
                SelectedForRating = anime;
                CurrentRatingAnime = anime;
                SelectedTabIndex = 1;
                OnPropertyChanged(nameof(SelectedForRating));
                OnPropertyChanged(nameof(CurrentRatingAnime));
                RefreshPlanView();
                UpdateAllStatistics();
            }
        }

        public void HandlePendingRating()
        {
            if (SelectedForRating == null) return;

            if (_isReEvaluating)
            {
                if (!TopList.Contains(SelectedForRating))
                {
                    TopList.Add(SelectedForRating);
                    SortTopList();
                }
            }
            else
            {
                SelectedForRating.Status = LocalizationManager.CurrentLanguage == "English" ? "Watching" : "Смотрю";
                if (!AnimeList.Contains(SelectedForRating))
                    AnimeList.Add(SelectedForRating);
                RefreshPlanView();
            }

            SelectedForRating = null;
            CurrentRatingAnime = null;
            OnPropertyChanged(nameof(CurrentRatingAnime));
            _isReEvaluating = false;
        }

        // Классы статистики
        public class GenreStat : INotifyPropertyChanged
        {
            private int _count;
            public int Count
            {
                get => _count;
                set { _count = value; OnPropertyChanged(); OnPropertyChanged(nameof(PercentWidth)); }
            }
            private int _maxCount;
            public int MaxCount
            {
                get => _maxCount;
                set { _maxCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(PercentWidth)); }
            }
            private double _score;
            public double Score
            {
                get => _score;
                set { _score = value; OnPropertyChanged(); OnPropertyChanged(nameof(ScorePercentWidth)); }
            }
            public string Genre { get; set; } = "";
            public double PercentWidth => MaxCount > 0 ? (double)Count / MaxCount * 200 : 0;
            public double ScorePercentWidth => Score * 20;

            public event PropertyChangedEventHandler? PropertyChanged;
            protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public class YearTopEntry
        {
            public string Year { get; set; } = "";
            public string TopTitle { get; set; } = "";
            public double TopScore { get; set; }
        }
    }
}