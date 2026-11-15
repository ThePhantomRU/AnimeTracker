using AnimeTracker.Models;
using AnimeTracker.Services;
using AnimeTracker.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AnimeTracker.Views
{
    /// <summary>
    /// Окно «Редактировать аниме». Работает с копией данных (<see cref="EditingAnime"/> и временные
    /// коллекции частей): оригинал изменяется только после нажатия «Сохранить» и успешной проверки.
    /// Общая логика форм вынесена в <see cref="AnimeFormHelper"/>.
    /// </summary>
    public partial class EditAnimeWindow : Window, INotifyPropertyChanged
    {
        // Ограничения формы
        private const int MaxPartsCount = 100;        // максимум сезонов / фильмов / OVA / ONA
        private const int MaxAdditionalGenres = 50;   // максимум дополнительных жанров

        /// <summary>Редактируемая копия аниме (привязана к полям формы).</summary>
        public Anime EditingAnime { get; set; }

        // ВАЖНО: канонические (русские) ключи жанра/статуса, а НЕ отображаемый
        // текст. Не переводятся и не меняются при переключении языка
        // интерфейса - см. подробный комментарий в LocalizedOption.cs.
        // ComboBox в XAML привязан к ним через SelectedValue/SelectedValuePath,
        // а не через SelectedItem, поэтому пересоздание списков GenreOptions/
        // StatusOptions на новом языке никогда не сбрасывает выбор.
        //
        // Существующие данные (anime.Genre / anime.Status) могли быть
        // сохранены как на русском, так и на английском - в зависимости от
        // того, какой язык был активен в момент последнего сохранения.
        // GetRussianGenre/GetRussianStatus безопасно нормализуют оба варианта
        // к каноническому русскому ключу (если строка уже русская - она
        // просто возвращается как есть, см. default-ветку switch).
        private string _genreKey = "";
        public string GenreKey
        {
            get => _genreKey;
            set { _genreKey = value; OnPropertyChanged(); }
        }

        private List<LocalizedOption> _genreOptions = new();
        public List<LocalizedOption> GenreOptions
        {
            get => _genreOptions;
            private set { _genreOptions = value; OnPropertyChanged(); }
        }

        private string _statusKey = "План";
        public string StatusKey
        {
            get => _statusKey;
            set { _statusKey = value; OnPropertyChanged(); }
        }

        private List<LocalizedOption> _statusOptions = new();
        public List<LocalizedOption> StatusOptions
        {
            get => _statusOptions;
            private set { _statusOptions = value; OnPropertyChanged(); }
        }

        // Подавляет реентрантные UpdateAdditionalGenreItemsSources() из
        // SelectionChanged во время пакетного обновления в OnLanguageChanged.
        private bool _isSyncingLanguage = false;

        public ObservableCollection<AdditionalGenreItem> TempAdditionalGenres { get; set; } = new();

        private int seasonsCount;
        public int SeasonsCount { get => seasonsCount; set { seasonsCount = Math.Clamp(value, 0, MaxPartsCount); UpdateSeasonsCollection(); OnPropertyChanged(); } }
        private int moviesCount;
        public int MoviesCount { get => moviesCount; set { moviesCount = Math.Clamp(value, 0, MaxPartsCount); UpdateMoviesCollection(); OnPropertyChanged(); } }
        private int ovasCount;
        public int OvasCount { get => ovasCount; set { ovasCount = Math.Clamp(value, 0, MaxPartsCount); UpdateOvasCollection(); OnPropertyChanged(); } }
        private int onasCount;
        public int OnasCount { get => onasCount; set { onasCount = Math.Clamp(value, 0, MaxPartsCount); UpdateOnasCollection(); OnPropertyChanged(); } }

        public ObservableCollection<Season> TempSeasons { get; set; } = new();
        public ObservableCollection<Movie> TempMovies { get; set; } = new();
        public ObservableCollection<Ova> TempOvas { get; set; } = new();
        public ObservableCollection<Ona> TempOnas { get; set; } = new();

        /// <summary>Оригинальное аниме из списка: изменяется только при сохранении.</summary>
        private readonly Anime originalAnime;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public EditAnimeWindow(Anime anime)
        {
            InitializeComponent();
            originalAnime = anime;

            GenreOptions = AnimeFormHelper.BuildGenreOptions();
            StatusOptions = AnimeFormHelper.BuildStatusOptions();

            // Нормализуем уже сохранённые жанр/статус (могут быть на любом
            // из двух языков) к каноническому русскому ключу.
            GenreKey = MainViewModel.GetRussianGenre(anime.Genre);
            StatusKey = MainViewModel.GetRussianStatus(anime.Status);

            EditingAnime = new Anime
            {
                Title = anime.Title,
                Genre = anime.Genre,
                Status = anime.Status,
                Description = anime.Description,
                VoiceOver = anime.VoiceOver,
                AdditionalGenres = new List<string>(anime.AdditionalGenres),
                Seasons = anime.Seasons.Select(s => new Season { EpisodeCount = s.EpisodeCount, Year = s.Year }).ToList(),
                Movies = anime.Movies.Select(m => new Movie { Year = m.Year }).ToList(),
                Ovas = anime.Ovas.Select(o => new Ova { EpisodeCount = o.EpisodeCount, Year = o.Year }).ToList(),
                Onas = anime.Onas.Select(o => new Ona { EpisodeCount = o.EpisodeCount, Year = o.Year }).ToList()
            };

            for (int i = 0; i < anime.AdditionalGenres.Count; i++)
                TempAdditionalGenres.Add(new AdditionalGenreItem
                {
                    Index = i + 1,
                    SelectedGenreKey = MainViewModel.GetRussianGenre(anime.AdditionalGenres[i])
                });

            // Заполняем черновики частей сразу готовыми объектами. Раньше сначала создавались пустые
            // заготовки, а затем каждая заменялась копией — лишняя работа и лишние уведомления коллекций.
            // (DataContext ещё не назначен, поэтому уведомлять об изменении счётчиков не нужно.)
            seasonsCount = Math.Clamp(anime.Seasons.Count, 0, MaxPartsCount);
            foreach (var s in anime.Seasons.Take(seasonsCount))
                TempSeasons.Add(new Season { EpisodeCount = s.EpisodeCount, Year = s.Year });

            moviesCount = Math.Clamp(anime.Movies.Count, 0, MaxPartsCount);
            foreach (var m in anime.Movies.Take(moviesCount))
                TempMovies.Add(new Movie { Year = m.Year });

            ovasCount = Math.Clamp(anime.Ovas.Count, 0, MaxPartsCount);
            foreach (var o in anime.Ovas.Take(ovasCount))
                TempOvas.Add(new Ova { EpisodeCount = o.EpisodeCount, Year = o.Year });

            onasCount = Math.Clamp(anime.Onas.Count, 0, MaxPartsCount);
            foreach (var o in anime.Onas.Take(onasCount))
                TempOnas.Add(new Ona { EpisodeCount = o.EpisodeCount, Year = o.Year });

            DataContext = this;
            LocalizationManager.LanguageChanged += OnLanguageChanged;

            AnimeFormHelper.UpdateAdditionalGenreItemsSources(GenreKey, TempAdditionalGenres);
            AnimeFormHelper.AttachPasteGuards(this);
        }

        private void OnLanguageChanged()
        {
            // Пересчитываем только ОТОБРАЖАЕМЫЙ текст вариантов - канонические
            // ключи (GenreKey, StatusKey, SelectedGenreKey у доп. жанров)
            // вообще не трогаем, поэтому им нечего "конвертировать" и нечего
            // "не найти" в новом списке: SelectedValue ищет совпадение по Key,
            // а Key не менялся.
            _isSyncingLanguage = true;
            try
            {
                GenreOptions = AnimeFormHelper.BuildGenreOptions();
                StatusOptions = AnimeFormHelper.BuildStatusOptions();
                AnimeFormHelper.UpdateAdditionalGenreItemsSources(GenreKey, TempAdditionalGenres);
            }
            finally
            {
                _isSyncingLanguage = false;
            }
        }

        // ───────────── Части: сезоны, фильмы, OVA, ONA ─────────────

        private void UpdateSeasonsCollection() => AnimeFormHelper.ResizeList(TempSeasons, SeasonsCount, () => new Season());
        private void UpdateMoviesCollection() => AnimeFormHelper.ResizeList(TempMovies, MoviesCount, () => new Movie());
        private void UpdateOvasCollection() => AnimeFormHelper.ResizeList(TempOvas, OvasCount, () => new Ova { EpisodeCount = 1 });
        private void UpdateOnasCollection() => AnimeFormHelper.ResizeList(TempOnas, OnasCount, () => new Ona { EpisodeCount = 1 });

        // ───────────── Обработчики элементов формы (подключены в XAML) ─────────────

        private void Genre_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSyncingLanguage) return;
            AnimeFormHelper.UpdateAdditionalGenreItemsSources(GenreKey, TempAdditionalGenres);
        }

        private void AdditionalGenre_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSyncingLanguage) return;
            AnimeFormHelper.UpdateAdditionalGenreItemsSources(GenreKey, TempAdditionalGenres);
        }

        private void AddAdditionalGenre_Click(object sender, RoutedEventArgs e)
        {
            if (TempAdditionalGenres.Count >= MaxAdditionalGenres) return;
            TempAdditionalGenres.Add(new AdditionalGenreItem { Index = TempAdditionalGenres.Count + 1 });
            AnimeFormHelper.UpdateAdditionalGenreItemsSources(GenreKey, TempAdditionalGenres);
        }

        private void RemoveAdditionalGenre_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is AdditionalGenreItem item)
            {
                TempAdditionalGenres.Remove(item);
                for (int i = 0; i < TempAdditionalGenres.Count; i++)
                    TempAdditionalGenres[i].Index = i + 1;
                AnimeFormHelper.UpdateAdditionalGenreItemsSources(GenreKey, TempAdditionalGenres);
            }
        }

        private void NumberOnlyTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
            => e.Handled = !int.TryParse(e.Text, out _);

        private void DescriptionTextBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
            => AnimeFormHelper.ScrollTextBoxWithWheel(sender, e);

        // ───────────── Сохранение ─────────────

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (!AnimeFormHelper.ValidateForm(EditingAnime.Title, GenreKey, EditingAnime.Description, EditingAnime.VoiceOver,
                                              SeasonsCount, MoviesCount, OvasCount, OnasCount,
                                              TempSeasons, TempMovies, TempOvas, TempOnas))
                return;

            var vm = MainViewModel.Instance;
            bool wasInTop = vm.TopList.Contains(originalAnime);

            // Сохраняем жанр/статус в ТЕКУЩЕМ языке интерфейса - точно так же,
            // как это делалось раньше. GenreKey/StatusKey/SelectedGenreKey -
            // канонические (русские), переводим их в момент сохранения.
            string newGenre = AnimeFormHelper.ToStoredGenre(GenreKey);
            string newStatus = AnimeFormHelper.ToStoredStatus(StatusKey);
            var newAdditionalGenres = TempAdditionalGenres
                .Where(x => !string.IsNullOrWhiteSpace(x.SelectedGenreKey))
                .Select(x => AnimeFormHelper.ToStoredGenre(x.SelectedGenreKey!))
                .ToList();

            // 1) Переносим изменения в оригинал
            originalAnime.Title = EditingAnime.Title;
            originalAnime.Genre = newGenre;
            originalAnime.Description = EditingAnime.Description;
            originalAnime.VoiceOver = EditingAnime.VoiceOver;
            originalAnime.AdditionalGenres = newAdditionalGenres;

            // Существующие части (с их оценками) сохраняются, обновляются только год и число серий
            AnimeFormHelper.SyncParts(originalAnime.Seasons, TempSeasons, () => new Season(),
                (dst, src) => { dst.EpisodeCount = src.EpisodeCount; dst.Year = src.Year; });
            AnimeFormHelper.SyncParts(originalAnime.Movies, TempMovies, () => new Movie(),
                (dst, src) => dst.Year = src.Year);
            AnimeFormHelper.SyncParts(originalAnime.Ovas, TempOvas, () => new Ova { EpisodeCount = 1 },
                (dst, src) => { dst.EpisodeCount = src.EpisodeCount; dst.Year = src.Year; });
            AnimeFormHelper.SyncParts(originalAnime.Onas, TempOnas, () => new Ona { EpisodeCount = 1 },
                (dst, src) => { dst.EpisodeCount = src.EpisodeCount; dst.Year = src.Year; });

            originalAnime.Status = newStatus;

            bool isWatched = newStatus == "Просмотрено" || newStatus == "Watched";

            // 2) Аниме из топа переведено в «не просмотрено» — убираем из топа, оценки сохраняем как временные
            if (wasInTop && !isWatched)
            {
                vm.TopList.Remove(originalAnime);
                vm.SortTopList();
                foreach (var part in originalAnime.GetAllParts())
                    AnimeFormHelper.CopyMainScoresToTemp(part);
            }

            // 3) Размещаем аниме в нужном списке согласно статусу
            if (isWatched)
            {
                if (!wasInTop)
                {
                    // Только что отмечено как просмотренное — отправляем на оценивание
                    originalAnime.ApplyTempScores();
                    vm.AnimeList.Remove(originalAnime);
                    vm.SelectedForRating = originalAnime;
                    vm.SelectedTabIndex = 1;
                }
                else
                {
                    // Уже в топе — просто пересортировываем
                    originalAnime.ApplyTempScores();
                    vm.SortTopList();
                }
            }
            else
            {
                if (!vm.AnimeList.Contains(originalAnime) && !vm.TopList.Contains(originalAnime))
                    vm.AnimeList.Add(originalAnime);
            }

            vm.RefreshPlanView();
            vm.UpdateAllStatistics();
            vm.SaveDataToFile();

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        protected override void OnClosed(EventArgs e)
        {
            LocalizationManager.LanguageChanged -= OnLanguageChanged;
            AnimeFormHelper.DetachPasteGuards(this);
            base.OnClosed(e);
        }
    }
}