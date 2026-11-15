using AnimeTracker.Models;
using AnimeTracker.Services;
using AnimeTracker.ViewModels;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace AnimeTracker
{
    public partial class ConflictResolutionWindow : Window, INotifyPropertyChanged
    {
        private Anime _anime1;
        private Anime _anime2;
        private bool _canClose;

        public Anime Anime1
        {
            get => _anime1;
            set { _anime1 = value; OnPropertyChanged(); OnPropertyChanged(nameof(Anime1DisplayGenre)); }
        }

        public Anime Anime2
        {
            get => _anime2;
            set { _anime2 = value; OnPropertyChanged(); OnPropertyChanged(nameof(Anime2DisplayGenre)); }
        }

        public int SelectedWinner { get; private set; }

        public double DisplayScore1 => Anime1.AdjustedScore > 0 ? Anime1.AdjustedScore : Anime1.Score;
        public double DisplayScore2 => Anime2.AdjustedScore > 0 ? Anime2.AdjustedScore : Anime2.Score;

        public string Anime1DisplayGenre => GetLocalizedGenre(Anime1?.Genre);
        public string Anime2DisplayGenre => GetLocalizedGenre(Anime2?.Genre);

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public ConflictResolutionWindow(Anime anime1, Anime anime2)
        {
            InitializeComponent();
            Anime1 = anime1;
            Anime2 = anime2;
            DataContext = this;
            Closing += Window_Closing;
            LocalizationManager.LanguageChanged += OnLanguageChanged;
        }

        private void OnLanguageChanged()
        {
            OnPropertyChanged(nameof(Anime1DisplayGenre));
            OnPropertyChanged(nameof(Anime2DisplayGenre));
        }

        private string GetLocalizedGenre(string? genre)
        {
            if (string.IsNullOrEmpty(genre)) return "";
            bool isEnglish = LocalizationManager.CurrentLanguage == "English";
            return isEnglish ? MainViewModel.GetEnglishGenre(genre) : genre;
        }

        private void Window_Closing(object? sender, CancelEventArgs e)
        {
            if (!_canClose) e.Cancel = true;
        }

        private void SelectLeft_Click(object sender, RoutedEventArgs e)
        {
            SelectedWinner = 1;
            _canClose = true;
            DialogResult = true;
            Close();
        }

        private void SelectRight_Click(object sender, RoutedEventArgs e)
        {
            SelectedWinner = 2;
            _canClose = true;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            _canClose = true;
            DialogResult = false;
            Close();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }

        protected override void OnClosed(EventArgs e)
        {
            LocalizationManager.LanguageChanged -= OnLanguageChanged;
            base.OnClosed(e);
        }
    }
}