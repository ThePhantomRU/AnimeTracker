using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using AnimeTracker.Services;
using AnimeTracker.ViewModels;

namespace AnimeTracker.Views
{
    public partial class BackupManagerWindow : Window
    {
        public ObservableCollection<BackupInfo> Backups { get; } = new();

        public BackupManagerWindow()
        {
            InitializeComponent();
            DataContext = this;
            LoadBackups();
        }

        private void LoadBackups()
        {
            Backups.Clear();
            foreach (var b in BackupManager.GetBackups())
                Backups.Add(b);
        }

        private void Restore_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is BackupInfo backup)
            {
                bool isEnglish = LocalizationManager.CurrentLanguage == "English";
                string message = isEnglish
                    ? $"Restore data from backup dated {backup.DisplayDate}?\nCurrent data will be overwritten."
                    : $"Восстановить данные из копии от {backup.DisplayDate}?\nТекущие данные будут заменены.";
                string title = isEnglish ? "Confirmation" : "Подтверждение";
                string successMessage = isEnglish
                    ? "Data restored successfully."
                    : "Данные успешно восстановлены.";
                string successTitle = isEnglish ? "Success" : "Успех";

                var result = MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    if (BackupManager.RestoreFromBackup(backup.FilePath))
                    {
                        ReloadMainViewModelData();
                        MessageBox.Show(successMessage, successTitle, MessageBoxButton.OK, MessageBoxImage.Information);
                        Close();
                    }
                }
            }
        }

        private static void ReloadMainViewModelData()
        {
            var mainWindow = Application.Current.MainWindow;
            if (mainWindow != null && mainWindow.DataContext is MainViewModel vm)
            {
                vm.SelectedAnime = null;
                vm.SelectedForRating = null;
                vm.SelectedTopAnime = null;
                vm.ReloadData();
                vm.SelectedTabIndex = 0;
            }
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is BackupInfo backup)
            {
                bool isEnglish = LocalizationManager.CurrentLanguage == "English";
                string message = isEnglish
                    ? $"Delete backup {backup.FileName}?"
                    : $"Удалить резервную копию {backup.FileName}?";
                string title = isEnglish ? "Confirmation" : "Подтверждение";

                var result = MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    BackupManager.DeleteBackup(backup.FilePath);
                    LoadBackups();
                }
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}