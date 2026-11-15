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
using System.Windows.Threading;

namespace AnimeTracker.Views
{
    /// <summary>
    /// Окно «Добавить аниме». Пользователь вводит название, жанры, описание, озвучку и состав
    /// (сезоны, фильмы, OVA, ONA); после проверки аниме добавляется в список плана и сохраняется на диск.
    /// Общая логика форм (списки жанров, проверка, копирование частей) вынесена в <see cref="AnimeFormHelper"/>.
    /// </summary>
    public partial class AddAnimeWindow : Window, INotifyPropertyChanged
    {
        // Ограничения формы
        private const int MaxPartsCount = 100;        // максимум сезонов / фильмов / OVA / ONA
        private const int MaxAdditionalGenres = 50;   // максимум дополнительных жанров

        private string _animeTitle = "";
        public string AnimeTitle
        {
            get => _animeTitle;
            set { _animeTitle = value; OnPropertyChanged(); }
        }

        // ВАЖНО: канонический (русский) ключ жанра, а НЕ отображаемый текст.
        // Не переводится и не меняется при переключении языка интерфейса -
        // см. подробный комментарий в LocalizedOption.cs. ComboBox в XAML
        // привязан к нему через SelectedValue/SelectedValuePath, а не через
        // SelectedItem, поэтому пересоздание списка GenreOptions на новом
        // языке никогда не сбрасывает выбор.
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

        private string _description = "";
        public string Description
        {
            get => _description;
            set { _description = value; OnPropertyChanged(); }
        }

        private string _voiceOver = "";
        public string VoiceOver
        {
            get => _voiceOver;
            set { _voiceOver = value; OnPropertyChanged(); }
        }

        // Канонический (русский) ключ статуса: "План" / "Смотрю" / "Просмотрено".
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

        /// <summary>Созданное аниме (после успешного добавления); null, если окно закрыто без добавления.</summary>
        public Anime? AddedAnime { get; private set; }

        // Подавляет реентрантные UpdateAdditionalGenreItemsSources() из
        // SelectionChanged во время пакетного обновления в OnLanguageChanged.
        private bool _isSyncingLanguage = false;

        public ObservableCollection<AdditionalGenreItem> TempAdditionalGenres { get; set; } = new();

        private int seasonsCount;
        public int SeasonsCount
        {
            get => seasonsCount;
            set { seasonsCount = Math.Clamp(value, 0, MaxPartsCount); UpdateSeasonsCollection(); OnPropertyChanged(); }
        }
        private int moviesCount;
        public int MoviesCount
        {
            get => moviesCount;
            set { moviesCount = Math.Clamp(value, 0, MaxPartsCount); UpdateMoviesCollection(); OnPropertyChanged(); }
        }
        private int ovasCount;
        public int OvasCount
        {
            get => ovasCount;
            set { ovasCount = Math.Clamp(value, 0, MaxPartsCount); UpdateOvasCollection(); OnPropertyChanged(); }
        }
        private int onasCount;
        public int OnasCount
        {
            get => onasCount;
            set { onasCount = Math.Clamp(value, 0, MaxPartsCount); UpdateOnasCollection(); OnPropertyChanged(); }
        }

        // Черновики частей: пользователь вводит год и число серий, а при добавлении они копируются в аниме
        public ObservableCollection<Season> TempSeasons { get; set; } = new();
        public ObservableCollection<Movie> TempMovies { get; set; } = new();
        public ObservableCollection<Ova> TempOvas { get; set; } = new();
        public ObservableCollection<Ona> TempOnas { get; set; } = new();

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public AddAnimeWindow()
        {
            InitializeComponent();
            DataContext = this;

            GenreOptions = AnimeFormHelper.BuildGenreOptions();
            StatusOptions = AnimeFormHelper.BuildStatusOptions();
            StatusKey = "План"; // канонический ключ по умолчанию, не зависит от языка

            AnimeFormHelper.AttachPasteGuards(this);

            AnimeFormHelper.UpdateAdditionalGenreItemsSources(GenreKey, TempAdditionalGenres);

            LocalizationManager.LanguageChanged += OnLanguageChanged;
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

        private void NumberOnlyTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
            => e.Handled = !int.TryParse(e.Text, out _);

        private void DescriptionTextBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
            => AnimeFormHelper.ScrollTextBoxWithWheel(sender, e);

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
                // Перенумеровываем оставшиеся поля
                for (int i = 0; i < TempAdditionalGenres.Count; i++)
                    TempAdditionalGenres[i].Index = i + 1;
                AnimeFormHelper.UpdateAdditionalGenreItemsSources(GenreKey, TempAdditionalGenres);
            }
        }

        // ───────────── Добавление ─────────────

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            if (!AnimeFormHelper.ValidateForm(AnimeTitle, GenreKey, Description, VoiceOver,
                                              SeasonsCount, MoviesCount, OvasCount, OnasCount,
                                              TempSeasons, TempMovies, TempOvas, TempOnas))
                return;

            // Сохраняем жанр/статус в ТЕКУЩЕМ языке интерфейса - точно так же,
            // как это делалось раньше (поведение хранения данных не
            // изменилось, изменился только способ, которым ComboBox работает
            // с выбором внутри окна). GenreKey/StatusKey/SelectedGenreKey -
            // канонические (русские), переводим их в момент сохранения.
            var additionalGenres = TempAdditionalGenres
                .Where(x => !string.IsNullOrWhiteSpace(x.SelectedGenreKey))
                .Select(x => AnimeFormHelper.ToStoredGenre(x.SelectedGenreKey!))
                .ToList();

            Anime anime = new()
            {
                Title = AnimeTitle.Trim(),
                Genre = AnimeFormHelper.ToStoredGenre(GenreKey),
                AdditionalGenres = additionalGenres,
                Status = AnimeFormHelper.ToStoredStatus(StatusKey),
                Description = Description.Trim(),
                VoiceOver = VoiceOver.Trim()
            };

            foreach (var s in TempSeasons)
                anime.Seasons.Add(new Season { EpisodeCount = s.EpisodeCount, Year = s.Year });
            foreach (var m in TempMovies)
                anime.Movies.Add(new Movie { Year = m.Year });
            foreach (var o in TempOvas)
                anime.Ovas.Add(new Ova { EpisodeCount = o.EpisodeCount, Year = o.Year });
            foreach (var o in TempOnas)
                anime.Onas.Add(new Ona { EpisodeCount = o.EpisodeCount, Year = o.Year });

            var vm = MainViewModel.Instance;
            if (vm == null)
            {
                MessageBox.Show("Ошибка: ViewModel не инициализирована.");
                return;
            }

            AddedAnime = anime;

            vm.AnimeList.Add(anime);
            vm.SaveDataToFile();

            bool isWatched = anime.Status == "Просмотрено" || anime.Status == "Watched";

            DialogResult = true;
            Close();

            // Если аниме добавлено сразу как «Просмотрено» — открываем для него оценивание.
            // Делаем это после закрытия окна, когда интерфейс освободится (ApplicationIdle).
            if (isWatched)
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    vm.ProcessWatchedAnime(anime);
                }), DispatcherPriority.ApplicationIdle);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

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