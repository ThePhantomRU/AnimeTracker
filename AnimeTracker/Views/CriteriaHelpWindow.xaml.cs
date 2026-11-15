using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using AnimeTracker.Services;

namespace AnimeTracker.Views
{
    public partial class CriteriaHelpWindow : Window
    {
        public List<CriteriaGroup> CriteriaList { get; } = new();

        public CriteriaHelpWindow()
        {
            InitializeComponent();
            DataContext = this;
            LoadCriteria();
            LocalizationManager.LanguageChanged += LoadCriteria;
        }

        private void LoadCriteria()
        {
            CriteriaList.Clear();
            bool isEnglish = LocalizationManager.CurrentLanguage == "English";

            // Ключи критериев
            var keys = new[]
            {
                "Story",
                "MainCharacters",
                "Characters",
                "Visual",
                "Sound",
                "Opening",
                "Ending",
                "Emotions",
                "Originality",
                "Finale"
            };

            foreach (var key in keys)
            {
                // Название критерия
                string nameKey = $"Criteria_{key}";
                string name = TryFindResource(nameKey) as string ?? nameKey;

                var descriptions = new List<ScoreDescription>();

                // Для каждого балла 1–10 ищем описание
                for (int score = 1; score <= 10; score++)
                {
                    string descKey = $"Criteria_{key}_{score}";
                    string text = TryFindResource(descKey) as string;
                    if (!string.IsNullOrEmpty(text))
                        descriptions.Add(new ScoreDescription { Score = score, Text = text });
                }

                // Если описания не найдены — добавляем запасной вариант (но это не должно случиться)
                if (!descriptions.Any())
                {
                    for (int score = 1; score <= 10; score++)
                    {
                        descriptions.Add(new ScoreDescription
                        {
                            Score = score,
                            Text = isEnglish ? $"Score {score}" : $"Оценка {score}"
                        });
                    }
                }

                CriteriaList.Add(new CriteriaGroup { Name = name, Descriptions = descriptions });
            }
        }

        // Вспомогательный метод для поиска ресурса (сначала в словарях приложения, затем в словарях окна)
        private object? TryFindResource(string key)
        {
            // Пытаемся найти в глобальных ресурсах приложения
            if (Application.Current.Resources.Contains(key))
                return Application.Current.Resources[key];

            // Если не найдено, ищем в словарях MergedDictionaries (на случай, если словарь ещё не добавлен в Application.Resources)
            foreach (var dict in Application.Current.Resources.MergedDictionaries)
            {
                if (dict.Contains(key))
                    return dict[key];
            }

            return null;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        protected override void OnClosed(EventArgs e)
        {
            LocalizationManager.LanguageChanged -= LoadCriteria;
            base.OnClosed(e);
        }
    }

    public class CriteriaGroup
    {
        public string Name { get; set; } = "";
        public List<ScoreDescription> Descriptions { get; set; } = new();
    }

    public class ScoreDescription
    {
        public int Score { get; set; }
        public string Text { get; set; } = "";
    }
}