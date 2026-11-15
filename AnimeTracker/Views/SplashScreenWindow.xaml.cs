using AnimeTracker.ViewModels;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Animation;

namespace AnimeTracker.Views
{
    public partial class SplashScreenWindow : Window
    {
        private CancellationTokenSource _cts = new();

        public SplashScreenWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var storyboard = (Storyboard)FindResource("LogoAnimation");
            storyboard.Completed += Storyboard_Completed;
            storyboard.Begin();
        }

        private async void Storyboard_Completed(object sender, EventArgs e)
        {
            try
            {
                await Task.Delay(1000, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            var vm = new MainViewModel();
            MainViewModel.Instance = vm;
            vm.LoadDataFromFile();

            Dispatcher.Invoke(() =>
            {
                var mainWindow = new MainWindow();
                mainWindow.DataContext = vm;
                Application.Current.MainWindow = mainWindow;
                mainWindow.Topmost = true;
                mainWindow.Show();
                mainWindow.Topmost = false;
                mainWindow.Activate();
                this.Close();
            });
        }

        protected override void OnClosed(EventArgs e)
        {
            _cts.Cancel();
            _cts.Dispose();
            base.OnClosed(e);
        }
    }
}