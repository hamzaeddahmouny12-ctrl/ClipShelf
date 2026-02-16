using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using ClipShelf.Models;

namespace ClipShelf.ViewModels
{
    public class MainViewModel : BaseViewModel
    {
        private ObservableCollection<ClipboardItem>? _clipboardHistory;
        private string? _searchText;
        private string? _lastClipboardText;
        private DispatcherTimer? _clipboardTimer;

        public ObservableCollection<ClipboardItem> ClipboardHistory
        {
            get => _clipboardHistory ??= new();
            set => SetProperty(ref _clipboardHistory, value);
        }

        public string? SearchText
        {
            get => _searchText;
            set => SetProperty(ref _searchText, value);
        }
        public ICommand SearchCommand { get; }

        public ICommand ClearHistoryCommand { get; }
        public ICommand OpenPhoneSyncCommand { get; }
        public ICommand OpenShutdownTimerCommand { get; }

        public MainViewModel()
        {
            ClipboardHistory = new ObservableCollection<ClipboardItem>();
            ClearHistoryCommand = new RelayCommand(ClearHistory);
            OpenPhoneSyncCommand = new RelayCommand(OpenPhoneSync);
            OpenShutdownTimerCommand = new RelayCommand(OpenShutdownTimer);
            SearchCommand = new RelayCommand(LaunchSearch);
            
            try
            {
                StartClipboardMonitoring();
            }
            catch
            {
                // Clipboard monitoring failed, but app can still work
            }
        }

        private void StartClipboardMonitoring()
        {
            _clipboardTimer = new DispatcherTimer();
            _clipboardTimer.Interval = TimeSpan.FromMilliseconds(500);
            _clipboardTimer.Tick += (s, e) => CheckClipboard();
            _clipboardTimer.Start();
        }

        private void CheckClipboard()
        {
            try
            {
                var iData = System.Windows.Clipboard.GetDataObject();
                if (iData == null)
                    return;

                // first check for image data
                if (iData.GetDataPresent(System.Windows.DataFormats.Bitmap))
                {
                    var bmpSource = iData.GetData(System.Windows.DataFormats.Bitmap) as System.Windows.Media.Imaging.BitmapSource;
                    if (bmpSource != null)
                    {
                        // convert BitmapSource to BitmapImage so it can be frozen/serialized
                        var bmp = new System.Windows.Media.Imaging.BitmapImage();
                        using (var mem = new System.IO.MemoryStream())
                        {
                            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmpSource));
                            encoder.Save(mem);
                            mem.Position = 0;

                            bmp.BeginInit();
                            bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                            bmp.StreamSource = mem;
                            bmp.EndInit();
                            bmp.Freeze();
                        }

                        AddClipboardItem(new ClipboardItem(bmp));
                        _lastClipboardText = null; // reset text sentinel
                        return; // we've handled the item
                    }
                }

                // existing text handling
                if (iData.GetDataPresent(System.Windows.DataFormats.Text))
                {
                    var currentText = (string?)iData.GetData(System.Windows.DataFormats.Text);
                    if (!string.IsNullOrEmpty(currentText) && currentText != _lastClipboardText)
                    {
                        _lastClipboardText = currentText;
                        AddClipboardItem(new ClipboardItem(currentText));
                    }
                }
            }
            catch
            {
                // Clipboard access might fail sometimes
            }
        }

        private void ClearHistory()
        {
            ClipboardHistory.Clear();
        }

        private void OpenPhoneSync()
        {
            (System.Windows.Application.Current as App)?.OpenPhoneSync();
        }

        private void OpenShutdownTimer()
        {
            var win = new Views.ShutdownTimerWindow();
            win.Owner = System.Windows.Application.Current?.MainWindow;
            win.ShowDialog();
        }

        private void LaunchSearch()
        {
            if (string.IsNullOrWhiteSpace(SearchText))
                return;
            try
            {
                string query = Uri.EscapeDataString(SearchText);
                // use explorer "search-ms" protocol to search whole PC
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"search-ms:query={query}\"")
                {
                    UseShellExecute = true
                });
            }
            catch { }
        }

        public void AddClipboardItem(ClipboardItem item)
        {
            ClipboardHistory.Insert(0, item);
        }
    }

    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

        public void Execute(object? parameter) => _execute?.Invoke();
    }
}