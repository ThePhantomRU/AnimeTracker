using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using AnimeTracker.Services;

namespace AnimeTracker
{
    public partial class App : Application
    {
        private static readonly string SettingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AnimeTracker");
        private static readonly string SettingsFile = Path.Combine(SettingsDirectory, "appsettings.json");

        public static string CurrentThemeKey { get; private set; } = "Default";

        public static readonly string[] ThemeKeys = {
            "Default",
            "NightEmerald",
            "SunsetBlaze",
            "IcySapphire",
            "PinkDawn",
            "CarbonCharcoal",
            "AutumnAmber",
            "LavenderDream",
            "MidnightGalaxy"
        };

        public App()
        {
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show($"Необработанное исключение в UI-потоке:\n{e.Exception}", "Критическая ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception? ex = e.ExceptionObject as Exception;
            string message = $"Необработанное исключение домена:\n{ex?.ToString() ?? "Неизвестная ошибка"}";
            File.WriteAllText("crash.log", message);
            MessageBox.Show(message, "Критическая ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            string message = $"Необработанное исключение в задаче:\n{e.Exception}";
            File.AppendAllText("crash.log", message);
            e.SetObserved();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            string oldSettingsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
            if (!File.Exists(SettingsFile) && File.Exists(oldSettingsFile))
            {
                Directory.CreateDirectory(SettingsDirectory);
                File.Copy(oldSettingsFile, SettingsFile);
            }

            LocalizationManager.Initialize(SettingsFile);

            string themeKey = LoadTheme();
            ApplyTheme(themeKey);
        }

        private static string LoadTheme()
        {
            try
            {
                if (!File.Exists(SettingsFile)) return "Default";
                string json = File.ReadAllText(SettingsFile);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                string saved = settings?.Theme ?? "Default";
                return ThemeKeys.Contains(saved) ? saved : "Default";
            }
            catch
            {
                return "Default";
            }
        }

        public static void ApplyTheme(string themeKey)
        {
            if (!ThemeKeys.Contains(themeKey))
                themeKey = "Default";

            CurrentThemeKey = themeKey;

            string fileName = themeKey + "Theme.xaml";
            string themePath = $"Themes/{fileName}";

            var dict = new ResourceDictionary
            {
                Source = new Uri(themePath, UriKind.Relative)
            };

            var oldDict = Application.Current.Resources.MergedDictionaries
                .FirstOrDefault(d => d.Source != null && d.Source.OriginalString.Contains("Theme"));
            if (oldDict != null)
                Application.Current.Resources.MergedDictionaries.Remove(oldDict);

            Application.Current.Resources.MergedDictionaries.Add(dict);

            SaveTheme(themeKey);

            if (Application.Current.MainWindow is MainWindow mainWindow)
                mainWindow.Dispatcher.Invoke(mainWindow.SyncThemeComboBox);
        }

        public static void SaveTheme(string themeKey)
        {
            try
            {
                Directory.CreateDirectory(SettingsDirectory);
                AppSettings settings = LoadSettings();
                settings.Theme = themeKey;
                string json = JsonSerializer.Serialize(settings);
                File.WriteAllText(SettingsFile, json);
            }
            catch { }
        }

        private static AppSettings LoadSettings()
        {
            try
            {
                if (!File.Exists(SettingsFile)) return new AppSettings();
                string json = File.ReadAllText(SettingsFile);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
            catch
            {
                return new AppSettings();
            }
        }

        private class AppSettings
        {
            public string Theme { get; set; } = "Default";
            public string Language { get; set; } = "Русский";
        }
    }
}