using System.IO;
using ClipShelf.Models;
using InTheHand.Net;
using InTheHand.Net.Bluetooth;
using InTheHand.Net.Sockets;

namespace ClipShelf.Services;

/// <summary>Optional Bluetooth OBEX listener for receiving files from phone. Gracefully degrades if unavailable.</summary>
public class BluetoothSyncService : IDisposable
{
    private readonly string _saveFolder;
    private ObexListener? _listener;
    private volatile bool _running;
    private Thread? _listenThread;

    public event EventHandler<ReceivedFileItem>? FileReceived;
    public event EventHandler<string>? StatusChanged;

    public bool IsAvailable { get; private set; }
    public bool IsConnected { get; private set; }

    public BluetoothSyncService(string saveFolder)
    {
        _saveFolder = saveFolder;
        Directory.CreateDirectory(saveFolder);
    }

    public void Start()
    {
        if (_running) return;
        try
        {
            if (!BluetoothRadio.IsSupported)
            {
                StatusChanged?.Invoke(this, "Bluetooth not supported");
                return;
            }
            _listener = new ObexListener(ObexTransport.Bluetooth);
            _listener.Start();
            _running = true;
            IsAvailable = true;
            StatusChanged?.Invoke(this, "Listening for Bluetooth file transfers");
            _listenThread = new Thread(ListenLoop) { IsBackground = true };
            _listenThread.Start();
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            StatusChanged?.Invoke(this, $"Not available: {ex.Message}");
        }
    }

    private void ListenLoop()
    {
        while (_running && _listener != null)
        {
            try
            {
                var context = _listener.GetContext();
                if (context == null) continue;
                IsConnected = true;
                ProcessObexRequest(context);
            }
            catch (InvalidOperationException) when (!_running) { break; }
            catch (Exception)
            {
                IsConnected = false;
            }
        }
        IsConnected = false;
    }

    private void ProcessObexRequest(ObexListenerContext context)
    {
        var request = context.Request;
        string? fileName = (request.Headers != null ? request.Headers["Name"] : null) ?? Path.GetFileName(request.RawUrl?.TrimStart('/')) ?? "bluetooth_file";
        fileName = SanitizeFileName(fileName);
        if (string.IsNullOrEmpty(fileName)) fileName = "bluetooth_" + Guid.NewGuid().ToString("N")[..8];

        string fullPath = Path.Combine(_saveFolder, fileName);
        fullPath = EnsureUniquePath(fullPath);

        try
        {
            using var stream = request.InputStream;
            using var fs = File.Create(fullPath);
            stream.CopyTo(fs);
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Save failed: {ex.Message}");
            return;
        }

        var info = new FileInfo(fullPath);
        var item = new ReceivedFileItem
        {
            FileName = fileName,
            FullPath = fullPath,
            ReceivedAt = DateTime.Now,
            Source = "Bluetooth",
            SizeBytes = info.Length
        };
        FileReceived?.Invoke(this, item);
    }

    private static string SanitizeFileName(string name)
    {
        name = Path.GetFileName(name);
        if (string.IsNullOrEmpty(name)) return "";
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (char c in name)
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        return sb.ToString().Trim();
    }

    private static string EnsureUniquePath(string fullPath)
    {
        if (!File.Exists(fullPath)) return fullPath;
        string dir = Path.GetDirectoryName(fullPath)!;
        string baseName = Path.GetFileNameWithoutExtension(fullPath);
        string ext = Path.GetExtension(fullPath);
        for (int i = 1; i < 10000; i++)
        {
            var candidate = Path.Combine(dir, baseName + "_" + i + ext);
            if (!File.Exists(candidate)) return candidate;
        }
        return Path.Combine(dir, baseName + "_" + Guid.NewGuid().ToString("N")[..8] + ext);
    }

    public void Dispose()
    {
        _running = false;
        try
        {
            _listener?.Stop();
            _listener?.Close();
        }
        catch { }
    }
}
