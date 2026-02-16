using System;
using System.Diagnostics;
using System.Windows.Input;
using ClipShelf.Services;

namespace ClipShelf.ViewModels
{
    public class ShutdownTimerViewModel : BaseViewModel
    {
        private string _seconds = "";
        public string Seconds { get => _seconds; set => SetProperty(ref _seconds, value); }

        public ICommand StartCommand { get; }
        public ICommand CancelCommand { get; }

        public ShutdownTimerViewModel()
        {
            StartCommand = new RelayCommand(StartShutdown);
            CancelCommand = new RelayCommand(CancelShutdown);
        }

        private void StartShutdown()
        {
            if (!int.TryParse(Seconds, out int secs) || secs < 0)
            {
                System.Windows.MessageBox.Show("Please enter a valid non-negative number of seconds.", "Shutdown Timer", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            try
            {
                // schedule shutdown using system command
                Process.Start(new ProcessStartInfo("shutdown", "/s /t " + secs)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                });

                System.Windows.MessageBox.Show($"Shutdown scheduled in {secs} second(s).", "Shutdown Timer", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Failed to schedule shutdown: " + ex.Message, "Shutdown Timer", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void CancelShutdown()
        {
            try
            {
                Process.Start(new ProcessStartInfo("shutdown", "/a")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
                System.Windows.MessageBox.Show("Shutdown cancelled.", "Shutdown Timer", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Failed to cancel shutdown: " + ex.Message, "Shutdown Timer", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }
    }
}