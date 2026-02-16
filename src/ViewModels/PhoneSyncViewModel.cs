using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using ClipShelf.Models;
using ClipShelf.Services;

namespace ClipShelf.ViewModels;

public class PhoneSyncViewModel : BaseViewModel
{
    private string _localIp = "";
    private string _pin = "";
    private string _connectionStatus = "Disconnected";
    private BitmapImage? _qrCodeImage;
    private BackgroundAgent? _agent;

    public string LocalIp { get => _localIp; set => SetProperty(ref _localIp, value); }
    public string Pin { get => _pin; set => SetProperty(ref _pin, value); }
    public string ConnectionStatus { get => _connectionStatus; set => SetProperty(ref _connectionStatus, value); }
    public BitmapImage? QrCodeImage { get => _qrCodeImage; set => SetProperty(ref _qrCodeImage, value); }
    public string BluetoothStatus { get; set; } = "—";

    public ObservableCollection<ReceivedFileItem> ReceivedFiles { get; } = new();
    public ICommand RegeneratePinCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand ShareFilesCommand { get; }
    public ICommand AllowFirewallCommand { get; }

    public PhoneSyncViewModel(BackgroundAgent? agent)
    {
        _agent = agent;
        RegeneratePinCommand = new RelayCommand(RegeneratePin);
        OpenFolderCommand = new RelayCommand(OpenFolder);
        ShareFilesCommand = new RelayCommand(ShareFiles);
        AllowFirewallCommand = new RelayCommand(AllowFirewall);

        if (_agent != null)
        {
            LocalIp = NetworkHelper.GetLocalLanIp();
            Pin = _agent.CurrentPin;
            ConnectionStatus = _agent.IsWebServerRunning ? "Connected" : "Disconnected";
            BluetoothStatus = _agent.IsBluetoothAvailable ? (_agent.IsBluetoothConnected ? "Connected" : "Listening") : "Unavailable";

            foreach (var f in _agent.ReceivedFiles)
                ReceivedFiles.Add(f);
            _agent.FileReceived += (_, item) =>
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() => ReceivedFiles.Insert(0, item));
            };
            _agent.StatusChanged += (_, status) =>
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() => ConnectionStatus = status);
            };
            RefreshQrCode();
        }
        else
        {
            LocalIp = NetworkHelper.GetLocalLanIp();
            Pin = "——";
            ConnectionStatus = "Disconnected";
        }
    }

    public void RefreshQrCode()
    {
        if (string.IsNullOrEmpty(LocalIp) || string.IsNullOrEmpty(Pin)) return;
        int port = _agent?.WebPort ?? BackgroundAgent.DefaultWebPort;
        string url = $"http://{LocalIp}:{port}?pin={Pin}";
        QrCodeImage = QRCodeService.Generate(url, 280);
    }

    private void RegeneratePin()
    {
        _agent?.RegeneratePin();
        Pin = _agent?.CurrentPin ?? "";
        RefreshQrCode();
    }

    private void OpenFolder()
    {
        string folder = _agent?.UploadFolder ?? "";
        if (string.IsNullOrEmpty(folder) || !System.IO.Directory.Exists(folder))
            return;
        Process.Start("explorer.exe", folder);
    }

    private void ShareFiles()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog();
        dlg.Multiselect = true;
        if (dlg.ShowDialog() != true)
            return;
        try
        {
            string outDir = System.IO.Path.Combine(_agent?.UploadFolder ?? "", "Outgoing");
            System.IO.Directory.CreateDirectory(outDir);
            foreach (var f in dlg.FileNames)
            {
                var dest = System.IO.Path.Combine(outDir, System.IO.Path.GetFileName(f));
                System.IO.File.Copy(f, dest, true);
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show("Failed to share files: " + ex.Message, "ClipShelf", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private void AllowFirewall()
    {
        int port = _agent?.WebPort ?? BackgroundAgent.DefaultWebPort;
        if (NetworkHelper.TryAddFirewallRule(port))
        {
            System.Windows.MessageBox.Show("Firewall rule added. Try opening the link on your phone again.", "ClipShelf", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }
        string cmd = $"netsh advfirewall firewall add rule name=\"ClipShelf Phone Sync\" dir=in action=allow protocol=TCP localport={port}";
        try { System.Windows.Clipboard.SetText(cmd); } catch { }
        System.Windows.MessageBox.Show(
            "Connection timed out usually means Windows Firewall is blocking port " + port + ".\n\n" +
            "To fix:\n" +
            "1. Press Win, type cmd, right-click Command Prompt → Run as administrator.\n" +
            "2. Paste (Ctrl+V) and press Enter.\n" +
            "3. Try the QR code on your phone again.\n\n" +
            "The command has been copied to your clipboard.",
            "Allow ClipShelf in Firewall",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Warning);
    }
}
