using System.Collections.ObjectModel;
using System.IO;
using ClipShelf.Models;

namespace ClipShelf.Services;

/// <summary>Persistent background service: web server, Bluetooth, file log, auto-restart on crash.</summary>
public class BackgroundAgent : IDisposable
{
    private readonly string _uploadFolder;
    private readonly string _logFilePath;
    private readonly int _webPort;
    private SyncWebServer? _webServer;
    private BluetoothSyncService? _bluetooth;
    private volatile bool _running;
    private DateTime _lastPinRegeneratedAt;
    private readonly object _pinLock = new();
    private string _currentPin = "";
    private const int RestartDelayMs = 3000;

    public const int DefaultWebPort = 5080;

    public event EventHandler<ReceivedFileItem>? FileReceived;
    public event EventHandler<string>? StatusChanged;
    public event EventHandler<string>? LogReceived;

    public ObservableCollection<ReceivedFileItem> ReceivedFiles { get; } = new();
    public ObservableCollection<string> LogLines { get; } = new();
    public bool IsWebServerRunning => _webServer?.IsRunning ?? false;
    public bool IsBluetoothAvailable => _bluetooth?.IsAvailable ?? false;
    public bool IsBluetoothConnected => _bluetooth?.IsConnected ?? false;
    public string UploadFolder => _uploadFolder;
    public string OutgoingFolder => Path.Combine(_uploadFolder, "Outgoing");
    public int WebPort => _webPort;

    public string CurrentPin
    {
        get { lock (_pinLock) return _currentPin; }
        private set { lock (_pinLock) _currentPin = value; }
    }

    public DateTime PinCreatedAt { get; private set; }

    public BackgroundAgent(int webPort = DefaultWebPort)
    {
        _webPort = webPort;
        _uploadFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipShelf", "PhoneSync");
        _logFilePath = Path.Combine(_uploadFolder, "sync_log.txt");
        Directory.CreateDirectory(_uploadFolder);
        // ensure outgoing folder exists for PC-to-phone transfers
        Directory.CreateDirectory(Path.Combine(_uploadFolder, "Outgoing"));
        RegeneratePin();
    }

    public void RegeneratePin()
    {
        _webServer?.GeneratePin();
        CurrentPin = _webServer?.CurrentPin ?? GeneratePinInternal();
        PinCreatedAt = DateTime.UtcNow;
        _lastPinRegeneratedAt = DateTime.UtcNow;
        LogInternal("PIN regenerated (expires in 10 minutes if unused).");
    }

    private static string GeneratePinInternal()
    {
        return new string(Enumerable.Range(0, 6).Select(_ => (char)('0' + Random.Shared.Next(0, 10))).ToArray());
    }

    public void Start()
    {
        if (_running) return;
        _running = true;
        LogInternal("Background agent starting.");
        StartWebServerWithRestart();
        StartBluetoothIfAvailable();
    }

    private static bool _firewallRuleAttempted;

    private void StartWebServerWithRestart()
    {
        void Run()
        {
            while (_running)
            {
                try
                {
                    _webServer?.Dispose();
                    string bindIp = NetworkHelper.GetLocalLanIp();
                    if (!_firewallRuleAttempted)
                    {
                        _firewallRuleAttempted = true;
                        if (NetworkHelper.TryAddFirewallRule(_webPort))
                            LogInternal("Firewall rule added for port " + _webPort);
                        else
                            LogInternal("Tip: If phone cannot connect, allow ClipShelf or port " + _webPort + " in Windows Firewall.");
                    }
                    _webServer = new SyncWebServer(_webPort, _uploadFolder);
                    _webServer.CurrentPin = CurrentPin;
                    _webServer.FileReceived += OnWebFileReceived;
                    _webServer.LogMessage += (_, msg) => LogInternal(msg);
                    _webServer.Start(bindIp);
                    StatusChanged?.Invoke(this, "Connected");
                    while (_running && _webServer.IsRunning)
                        Thread.Sleep(500);
                }
                catch (Exception ex)
                {
                    LogInternal($"Web server error: {ex.Message}. Restarting in {RestartDelayMs / 1000}s...");
                    StatusChanged?.Invoke(this, "Disconnected");
                    Thread.Sleep(RestartDelayMs);
                }
            }
        }
        _ = Task.Run(Run);
    }

    private void OnWebFileReceived(object? sender, ReceivedFileEventArgs e)
    {
        var item = new ReceivedFileItem
        {
            FileName = e.FileName,
            FullPath = e.FullPath,
            ReceivedAt = DateTime.Now,
            Source = e.Source,
            SizeBytes = e.SizeBytes
        };
        AppendToFileLog(item);
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            ReceivedFiles.Insert(0, item);
        });
        FileReceived?.Invoke(this, item);
    }

    private void StartBluetoothIfAvailable()
    {
        try
        {
            _bluetooth = new BluetoothSyncService(_uploadFolder);
            _bluetooth.FileReceived += (_, item) =>
            {
                AppendToFileLog(item);
                System.Windows.Application.Current?.Dispatcher.Invoke(() => ReceivedFiles.Insert(0, item));
                FileReceived?.Invoke(this, item);
            };
            _bluetooth.StatusChanged += (_, msg) => LogInternal($"Bluetooth: {msg}");
            _bluetooth.Start();
        }
        catch (Exception ex)
        {
            LogInternal($"Bluetooth unavailable: {ex.Message}");
        }
    }

    private void AppendToFileLog(ReceivedFileItem item)
    {
        try
        {
            var line = $"{item.ReceivedAt:yyyy-MM-dd HH:mm:ss}\t{item.Source}\t{item.FileName}\t{item.SizeBytes}\r\n";
            File.AppendAllText(_logFilePath, line);
        }
        catch { }
    }

    private void LogInternal(string msg)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {msg}";
        try { File.AppendAllText(_logFilePath, line + "\r\n"); } catch { }
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            LogLines.Insert(0, line);
            if (LogLines.Count > 500) LogLines.RemoveAt(LogLines.Count - 1);
        });
        LogReceived?.Invoke(this, line);
    }

    public void Stop()
    {
        _running = false;
        _bluetooth?.Dispose();
        _webServer?.Stop();
        _webServer?.Dispose();
        LogInternal("Background agent stopped.");
    }

    public void Dispose() => Stop();
}
