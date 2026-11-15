using System.IO;
using System.Windows;
using System.Windows.Input;

namespace AnimeTracker.Views
{
    public partial class EulaWindow : Window
    {
        private bool _canClose = false;
        public bool Accepted { get; private set; } = false;

        public EulaWindow()
        {
            InitializeComponent();
            LoadLicenseText();
        }

        private void LoadLicenseText()
        {
            const string licenseFile = "LICENSE_RU.txt";
            if (File.Exists(licenseFile))
                LicenseTextBlock.Text = File.ReadAllText(licenseFile);
            else
                LicenseTextBlock.Text = "Лицензионное соглашение не найдено.";
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }

        private void Accept_Click(object sender, RoutedEventArgs e)
        {
            Accepted = true;
            _canClose = true;
            Close();
        }

        private void Decline_Click(object sender, RoutedEventArgs e)
        {
            Accepted = false;
            _canClose = true;
            Close();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_canClose) e.Cancel = true;
        }

        private void Window_StateChanged(object sender, System.EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
        }
    }
}