using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace AnimeTracker.Views
{
    public partial class ResetDataConfirmWindow : Window
    {
        private readonly DispatcherTimer _timer;
        private int _secondsLeft = 5;
        public bool Confirmed { get; private set; } = false;

        public ResetDataConfirmWindow()
        {
            InitializeComponent();
            UpdateButtonText();

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += Timer_Tick;
            _timer.Start();
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            _secondsLeft--;
            if (_secondsLeft <= 0)
            {
                _timer.Stop();
                OkButton.IsEnabled = true;
                OkButton.Content = (string)FindResource("ResetData_ConfirmButton");
            }
            else
            {
                UpdateButtonText();
            }
        }

        private void UpdateButtonText()
        {
            string baseText = (string)FindResource("ResetData_ConfirmButton");
            OkButton.Content = $"{baseText} ({_secondsLeft})";
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = true;
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = false;
            DialogResult = false;
            Close();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }

        protected override void OnClosed(EventArgs e)
        {
            _timer?.Stop();
            base.OnClosed(e);
        }
    }
}