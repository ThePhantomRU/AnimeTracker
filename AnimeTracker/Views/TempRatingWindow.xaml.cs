using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AnimeTracker.Models;
using AnimeTracker.Services;
using AnimeTracker.ViewModels;

namespace AnimeTracker.Views
{
    /// <summary>
    /// Окно «Предварительная оценка»: позволяет оценить аниме заранее (до или во время просмотра).
    /// <para>
    /// Принцип работы: пока окно открыто, пользователь двигает слайдеры основных оценок частей.
    /// «Сохранить» запоминает результат во временных оценках, «Отмена» возвращает основные оценки
    /// к последним сохранённым временным значениям. Когда аниме получает статус «Просмотрено»,
    /// временные оценки переносятся на вкладку «Оценка».
    /// </para>
    /// </summary>
    public partial class TempRatingWindow : Window, INotifyPropertyChanged
    {
        private readonly Anime _anime;
        public string AnimeTitle => _anime.Title;

        public ObservableCollection<PartViewModel> Parts { get; } = new();
        private Views.CriteriaHelpWindow? _criteriaHelpWindow;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public TempRatingWindow(Anime anime)
        {
            InitializeComponent();
            _anime = anime;
            DataContext = this;
            BuildPartsList();

            // Показываем в слайдерах ранее сохранённые временные оценки
            LoadTempScoresIntoMain();
            SaveMainScoresToTemp();

            PopulateTabControl();
            LocalizationManager.LanguageChanged += OnLanguageChanged;
        }

        private void OnLanguageChanged()
        {
            // Названия вкладок («Сезон 1», «Фильм 1») зависят от языка — пересобираем их заново
            // и возвращаем пользователя на ту же вкладку, на которой он находился.
            int selectedIndex = PartsTabControl.SelectedIndex;

            BuildPartsList();
            PopulateTabControl();

            if (selectedIndex >= 0 && selectedIndex < PartsTabControl.Items.Count)
                PartsTabControl.SelectedIndex = selectedIndex;
        }

        /// <summary>Формирует список частей (сезоны, затем фильмы) с локализованными названиями вкладок.</summary>
        private void BuildPartsList()
        {
            Parts.Clear();
            int idx = 1;
            string seasonLabel = MainViewModel.GetLocalizedString("Part_Season");
            string movieLabel = MainViewModel.GetLocalizedString("Part_Movie");

            foreach (var s in _anime.Seasons)
                Parts.Add(new PartViewModel { Part = s, DisplayName = $"{seasonLabel} {idx++}" });
            idx = 1;
            foreach (var m in _anime.Movies)
                Parts.Add(new PartViewModel { Part = m, DisplayName = $"{movieLabel} {idx++}" });
        }

        /// <summary>Создаёт по вкладке с панелью оценок для каждой части.</summary>
        private void PopulateTabControl()
        {
            PartsTabControl.Items.Clear();
            foreach (var part in Parts)
            {
                var panel = new PartRatingPanel { DataContext = part.Part };
                var scrollViewer = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Background = System.Windows.Media.Brushes.Transparent,
                    Content = panel
                };
                PartsTabControl.Items.Add(new TabItem { Header = part.DisplayName, Content = scrollViewer });
            }
            if (PartsTabControl.Items.Count > 0)
                PartsTabControl.SelectedIndex = 0;
        }

        // ───────────── Перенос оценок между основными и временными ─────────────

        /// <summary>Временные оценки → основные (для всех частей).</summary>
        private void LoadTempScoresIntoMain()
        {
            foreach (var part in _anime.GetAllParts())
                AnimeFormHelper.CopyTempScoresToMain(part);
        }

        /// <summary>Основные оценки → временные (запоминаем текущее состояние слайдеров).</summary>
        private void SaveMainScoresToTemp()
        {
            foreach (var part in _anime.GetAllParts())
                AnimeFormHelper.CopyMainScoresToTemp(part);
        }

        // ───────────── Обработчики окна (подключены в XAML) ─────────────

        private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer sv)
            {
                sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta / 3);
                e.Handled = true;
            }
        }

        private void ShowCriteriaHelp_Click(object sender, RoutedEventArgs e)
        {
            // Окно-справка существует в одном экземпляре: повторное нажатие просто выводит его на передний план
            if (_criteriaHelpWindow != null && _criteriaHelpWindow.IsLoaded)
            {
                _criteriaHelpWindow.Activate();
                return;
            }
            _criteriaHelpWindow = new Views.CriteriaHelpWindow();
            _criteriaHelpWindow.Owner = this;
            _criteriaHelpWindow.Closed += (s, args) => _criteriaHelpWindow = null;
            _criteriaHelpWindow.Show();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            SaveMainScoresToTemp();
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            // Откат: основные оценки возвращаются к последним сохранённым временным
            LoadTempScoresIntoMain();
            DialogResult = false;
            Close();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }
        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        protected override void OnClosed(System.EventArgs e)
        {
            LocalizationManager.LanguageChanged -= OnLanguageChanged;
            if (_criteriaHelpWindow != null && _criteriaHelpWindow.IsLoaded)
                _criteriaHelpWindow.Close();
            base.OnClosed(e);
        }
    }

    /// <summary>Обёртка части для списка вкладок: сама часть и подпись вкладки на текущем языке.</summary>
    public class PartViewModel : INotifyPropertyChanged
    {
        private PartBase _part = null!;
        public PartBase Part { get => _part; set { _part = value; OnPropertyChanged(); } }
        public string DisplayName { get; set; } = "";

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}