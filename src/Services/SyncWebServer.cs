using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ClipShelf.Services;

/// <summary>Lightweight HTTP server for phone uploads. PIN auth, 100MB limit, local-network only.</summary>
public class SyncWebServer : IDisposable
{
    private HttpListener? _listener;
    private volatile bool _running;
    private readonly int _port;
    private readonly string _uploadFolder;
    private readonly string _outgoingFolder;
    private readonly object _pinLock = new();
    private string _currentPin = "";
    private DateTime _pinCreatedAt;
    private const int PinExpiryMinutes = 10;
    private const long MaxUploadBytes = 100 * 1024 * 1024; // 100MB
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public event EventHandler<ReceivedFileEventArgs>? FileReceived;
    public event EventHandler<string>? LogMessage;

    public string CurrentPin
    {
        get { lock (_pinLock) return _currentPin; }
        set { lock (_pinLock) { _currentPin = value; _pinCreatedAt = DateTime.UtcNow; } }
    }

    public string UploadFolder => _uploadFolder;
    public int Port => _port;
    public bool IsRunning => _running;

    public SyncWebServer(int port, string uploadFolder)
    {
        _port = port;
        _uploadFolder = uploadFolder;
        Directory.CreateDirectory(_uploadFolder);
        // make outgoing subfolder where PC can place files for the phone
        _outgoingFolder = Path.Combine(_uploadFolder, "Outgoing");
        Directory.CreateDirectory(_outgoingFolder);
        CurrentPin = GeneratePin();
    }

    public string GeneratePin()
    {
        var pin = new string(Enumerable.Range(0, 6).Select(_ => (char)('0' + Random.Shared.Next(0, 10))).ToArray());
        lock (_pinLock) { _currentPin = pin; _pinCreatedAt = DateTime.UtcNow; }
        Log($"PIN regenerated: **** (expires in {PinExpiryMinutes} min)");
        return pin;
    }

    public bool IsPinValid(string? pin)
    {
        if (string.IsNullOrEmpty(pin)) return false;
        lock (_pinLock)
        {
            if (_currentPin != pin) return false;
            if ((DateTime.UtcNow - _pinCreatedAt).TotalMinutes > PinExpiryMinutes)
            {
                Log("PIN expired.");
                return false;
            }
        }
        return true;
    }

    /// <param name="bindAddress">Local IP to bind to (e.g. 192.168.1.10). If null/empty, uses all interfaces (may require admin on Windows).</param>
    public void Start(string? bindAddress = null)
    {
        if (_running) return;
        _listener = new HttpListener();
        string host = string.IsNullOrWhiteSpace(bindAddress) ? "+" : bindAddress.Trim();
        _listener.Prefixes.Add($"http://{host}:{_port}/");
        _listener.Start();
        _running = true;
        Log($"Web server started on {host}:{_port}");
        _ = AcceptLoopAsync();
    }

    public void Stop()
    {
        _running = false;
        try { _listener?.Stop(); _listener?.Close(); } catch { }
        Log("Web server stopped.");
    }

    private async Task AcceptLoopAsync()
    {
        while (_running && _listener != null)
        {
            try
            {
                var context = await _listener.GetContextAsync().ConfigureAwait(false);
                _ = Task.Run(() => HandleRequestAsync(context));
            }
            catch (HttpListenerException) when (!_running) { break; }
            catch (Exception ex) { Log($"Accept error: {ex.Message}"); }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;
        var clientIp = context.Request.RemoteEndPoint?.Address?.ToString() ?? "";

        if (!IsLocalNetwork(clientIp))
        {
            Log($"Rejected non-local connection from {clientIp}");
            await SendJsonAsync(response, 403, new { success = false, message = "Forbidden" }).ConfigureAwait(false);
            return;
        }

        string path = request.Url?.AbsolutePath ?? "/";
        string method = request.HttpMethod?.ToUpperInvariant() ?? "GET";

            // add new endpoints for listing and downloading files from outgoing folder
            if (path == "/files" && method == "GET")
            {
                await HandleListFilesAsync(context).ConfigureAwait(false);
                return;
            }
            if (path == "/download" && method == "GET")
            {
                await HandleDownloadAsync(context).ConfigureAwait(false);
                return;
            }

        try
        {
            if (path == "/ping" && method == "GET")
            {
                await SendJsonAsync(response, 200, new { ok = true, timestamp = DateTime.UtcNow }).ConfigureAwait(false);
                return;
            }

            if (path == "/status" && method == "GET")
            {
                bool pinValid = (DateTime.UtcNow - _pinCreatedAt).TotalMinutes <= PinExpiryMinutes;
                await SendJsonAsync(response, 200, new { running = true, pinExpired = !pinValid }).ConfigureAwait(false);
                return;
            }

            if (path == "/auth" && method == "POST")
            {
                string? pin = await ReadBodyAsJsonPinAsync(request).ConfigureAwait(false);
                bool valid = IsPinValid(pin);
                await SendJsonAsync(response, valid ? 200 : 401, new { success = valid }).ConfigureAwait(false);
                return;
            }

            if ((path == "/" || path == "") && method == "GET")
            {
                await SendUploadPageAsync(context).ConfigureAwait(false);
                return;
            }

            if (path == "/upload" && method == "POST")
            {
                await HandleUploadAsync(context).ConfigureAwait(false);
                return;
            }

            response.StatusCode = 404;
            response.Close();
        }
        catch (Exception ex)
        {
            Log($"Request error: {ex.Message}");
            await SendJsonAsync(response, 500, new { success = false, message = "Server error" }).ConfigureAwait(false);
        }
    }

    internal static bool IsLocalNetwork(string ipStr)
    {
        if (string.IsNullOrEmpty(ipStr) || ipStr == "127.0.0.1" || ipStr == "::1") return true;
        if (!IPAddress.TryParse(ipStr, out var ip) || ip == null) return false;
        byte[] b = ip.GetAddressBytes();
        if (b.Length == 4) // IPv4: 10.x, 172.16-31.x, 192.168.x
            return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168);
        return false;
    }

    private static async Task<string?> ReadBodyAsJsonPinAsync(HttpListenerRequest request)
    {
        if (!request.HasEntityBody) return null;
        using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
        var json = await reader.ReadToEndAsync().ConfigureAwait(false);
        var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("pin", out var p) ? p.GetString() : null;
    }

    private async Task SendUploadPageAsync(HttpListenerContext context)
    {
        var response = context.Response;
        string? pinFromQuery = context.Request.QueryString["pin"];
        string pinValue = string.IsNullOrEmpty(pinFromQuery) ? "" : pinFromQuery;
        string pinEscaped = pinValue.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        string html = GetUploadPageHtml(pinEscaped);
        byte[] bytes = Encoding.UTF8.GetBytes(html);
        response.StatusCode = 200;
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        response.Close();
    }

    private static string GetUploadPageHtml(string pinValue)
    {
        return "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">"
            + "<title>ClipShelf Phone Sync</title><style>*{margin:0;padding:0;} body{font-family:Segoe UI,Arial,sans-serif;background:#f0f4f8;padding:16px;} "
            + ".c{max-width:420px;margin:0 auto;background:#fff;border-radius:8px;padding:24px;} h1{color:#2E75B6;font-size:20px;margin-bottom:16px;} "
            + "input[type=password]{width:100%;padding:10px;margin-bottom:14px;} .z{border:2px dashed #2E75B6;border-radius:8px;padding:28px;text-align:center;cursor:pointer;} "
            + ".z:hover{background:#e8f2ff;} input[type=file]{display:none;} .btn{width:100%;padding:12px;background:#2E75B6;color:#fff;border:none;cursor:pointer;} "
            + "#msg{margin-top:12px;padding:10px;display:none;} #msg.ok{background:#d4edda;} #msg.err{background:#f8d7da;} .prog{height:6px;background:#eee;margin-top:8px;} .prog div{height:100%;background:#2E75B6;width:0%;}</style></head><body>"
            + "<div class=\"c\"><h1>ClipShelf – Send files</h1><label>PIN</label><input type=\"password\" id=\"pin\" placeholder=\"Enter PIN\" value=\"" + pinValue + "\">"
            + "<div class=\"z\" id=\"zone\"><p>Tap here or drag files (max 100 MB)</p></div><input type=\"file\" id=\"files\" multiple>"
            + "<button class=\"btn\" id=\"uploadBtn\">Upload</button><div class=\"prog\"><div id=\"progBar\"></div></div><div id=\"msg\"></div></div>"
            + "<h2>Available for download</h2><ul id=\"filesList\" style=\"list-style:none;padding:0;margin-top:12px;\"></ul>"
            + "<script>var zone=document.getElementById('zone'),files=document.getElementById('files'),pin=document.getElementById('pin'),btn=document.getElementById('uploadBtn'),msg=document.getElementById('msg'),progBar=document.getElementById('progBar');"
            + "zone.onclick=function(){files.click();}; zone.ondragover=function(e){e.preventDefault();zone.classList.add('dragover');};"
            + "zone.ondragleave=function(){zone.classList.remove('dragover');}; zone.ondrop=function(e){e.preventDefault();zone.classList.remove('dragover');files.files=e.dataTransfer.files;};"
            + "function showMsg(t,ok){msg.textContent=t;msg.className=ok?'ok':'err';msg.style.display='block';}"
            + "function upload(){var p=pin.value.trim();if(!p){showMsg('Enter PIN',false);return;} var fl=files.files;if(!fl.length){showMsg('Choose files',false);return;} "
            + "var fd=new FormData();fd.append('pin',p);for(var i=0;i<fl.length;i++) fd.append('files',fl[i]); msg.style.display='none'; progBar.style.width='0%';"
            + "var xhr=new XMLHttpRequest(); xhr.upload.onprogress=function(e){if(e.lengthComputable)progBar.style.width=(100*e.loaded/e.total)+'%';};"
            + "xhr.onload=function(){progBar.style.width='100%'; try{var r=JSON.parse(xhr.responseText); if(r.success){showMsg(r.message||'Done',true);files.value=''; fetchFiles();} else showMsg(r.message||'Failed',false);}catch{showMsg('Error',false);}};"
            + "xhr.onerror=function(){showMsg('Network error',false);}; xhr.open('POST','/upload?pin='+encodeURIComponent(p)); xhr.send(fd);} btn.onclick=upload;"
            + "function fetchFiles(){var p=pin.value.trim(); if(!p) return; fetch('/files?pin='+encodeURIComponent(p)).then(r=>r.json()).then(data=>{if(data.success){var list=document.getElementById('filesList'); list.innerHTML=''; data.files.forEach(f=>{var li=document.createElement('li'); var a=document.createElement('a'); a.href='/download?pin='+encodeURIComponent(p)+'&name='+encodeURIComponent(f.name); a.textContent=f.name; li.appendChild(a); list.appendChild(li);}); }}); }"
            + "pin.oninput = fetchFiles; window.onload = fetchFiles;</script></body></html>";
    }

    private async Task HandleUploadAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;
        string? pin = request.Headers["X-Pin"] ?? request.QueryString["pin"];
        string contentType = request.ContentType ?? "";

        if (!IsPinValid(pin))
        {
            await SendJsonAsync(response, 401, new { success = false, message = "Invalid or expired PIN" }).ConfigureAwait(false);
            return;
        }

        if (!contentType.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase))
        {
            await SendJsonAsync(response, 400, new { success = false, message = "Expected multipart/form-data" }).ConfigureAwait(false);
            return;
        }

        string? boundary = ParseBoundary(contentType);
        if (string.IsNullOrEmpty(boundary))
        {
            await SendJsonAsync(response, 400, new { success = false, message = "No boundary" }).ConfigureAwait(false);
            return;
        }

        long contentLength = request.ContentLength64;
        if (contentLength <= 0 || contentLength > MaxUploadBytes)
        {
            await SendJsonAsync(response, 413, new { success = false, message = "Upload size must be 1–100 MB" }).ConfigureAwait(false);
            return;
        }

        var received = new List<(string fileName, string fullPath, long size)>();
        using (var stream = request.InputStream)
        {
            var multipart = await ParseMultipartAsync(stream, boundary, contentLength).ConfigureAwait(false);
            foreach (var part in multipart)
            {
                if (string.IsNullOrEmpty(part.FileName)) continue;
                string safeName = SanitizeFileName(part.FileName);
                if (string.IsNullOrEmpty(safeName)) continue;
                string fullPath = Path.Combine(_uploadFolder, safeName);
                fullPath = EnsureUniquePath(fullPath);
                await File.WriteAllBytesAsync(fullPath, part.Data).ConfigureAwait(false);
                received.Add((safeName, fullPath, part.Data.LongLength));
            }
        }

        foreach (var (fileName, fullPath, size) in received)
        {
            var args = new ReceivedFileEventArgs(fileName, fullPath, size, "Web");
            FileReceived?.Invoke(this, args);
        }

        await SendJsonAsync(response, 200, new { success = true, count = received.Count, message = $"{received.Count} file(s) uploaded" }).ConfigureAwait(false);
    }

    // list files available for download (requires valid PIN)
    private async Task HandleListFilesAsync(HttpListenerContext context)
    {
        var response = context.Response;
        string? pin = context.Request.QueryString["pin"];
        if (!IsPinValid(pin))
        {
            await SendJsonAsync(response, 401, new { success = false, message = "Invalid or expired PIN" }).ConfigureAwait(false);
            return;
        }

        try
        {
            var files = Directory.Exists(_outgoingFolder)
                ? Directory.GetFiles(_outgoingFolder)
                        .Select(f => new { name = Path.GetFileName(f), size = new FileInfo(f).Length })
                        .ToArray()
                : Array.Empty<object>();
            await SendJsonAsync(response, 200, new { success = true, files }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await SendJsonAsync(response, 500, new { success = false, message = ex.Message }).ConfigureAwait(false);
        }
    }

    // download a specific file from the outgoing folder (requires PIN)
    private async Task HandleDownloadAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;
        string? pin = request.QueryString["pin"];
        if (!IsPinValid(pin))
        {
            await SendJsonAsync(response, 401, new { success = false, message = "Invalid or expired PIN" }).ConfigureAwait(false);
            return;
        }

        string? name = request.QueryString["name"];
        if (string.IsNullOrEmpty(name))
        {
            response.StatusCode = 400;
            response.Close();
            return;
        }
        // sanitize and restrict to outgoing folder
        name = Path.GetFileName(name);
        string fullPath = Path.Combine(_outgoingFolder, name);
        if (!File.Exists(fullPath))
        {
            response.StatusCode = 404;
            response.Close();
            return;
        }
        try
        {
            using var fs = File.OpenRead(fullPath);
            response.StatusCode = 200;
            string contentType = "application/octet-stream";
            response.ContentType = contentType;
            response.AddHeader("Content-Disposition", "attachment; filename=\"" + name + "\"");
            response.ContentLength64 = fs.Length;
            await fs.CopyToAsync(response.OutputStream).ConfigureAwait(false);
        }
        catch
        {
        }
        finally
        {
            response.Close();
        }
    }

    internal static string? ParseBoundary(string contentType)
    {
        const string key = "boundary=";
        int i = contentType.IndexOf(key, StringComparison.OrdinalIgnoreCase);
        if (i < 0) return null;
        var value = contentType[(i + key.Length)..].Trim();
        if (value.StartsWith('"')) value = value.Trim('"');
        // Take only the boundary token (some clients send "boundary=xxx; other")
        int end = value.IndexOfAny(new[] { ';', ' ', '\t', '\r', '\n' });
        if (end > 0) value = value[..end];
        return value.Trim();
    }

    private static async Task<List<Part>> ParseMultipartAsync(Stream stream, string boundary, long maxLength)
    {
        var parts = new List<Part>();
        byte[] boundaryCrlf = Encoding.UTF8.GetBytes("\r\n--" + boundary);
        byte[] boundaryLf = Encoding.UTF8.GetBytes("\n--" + boundary);
        byte[] endCrlf = Encoding.UTF8.GetBytes("\r\n--" + boundary + "--");
        byte[] endLf = Encoding.UTF8.GetBytes("\n--" + boundary + "--");
        var headerEndCrlf = new byte[] { (byte)'\r', (byte)'\n', (byte)'\r', (byte)'\n' };
        var headerEndLf = new byte[] { (byte)'\n', (byte)'\n' };
        var buffer = new byte[81920];
        var currentData = new MemoryStream();
        long readTotal = 0;
        string? currentFileName = null;
        string? currentName = null;

        while (readTotal < maxLength)
        {
            int toRead = (int)Math.Min(buffer.Length, maxLength - readTotal);
            int n = await stream.ReadAsync(buffer.AsMemory(0, toRead)).ConfigureAwait(false);
            if (n == 0) break;
            readTotal += n;

            int offset = 0;
            while (offset < n)
            {
                int idx = IndexOf(buffer, boundaryCrlf, offset, n);
                int skipLen = boundaryCrlf.Length;
                if (idx < 0)
                {
                    idx = IndexOf(buffer, boundaryLf, offset, n);
                    skipLen = boundaryLf.Length;
                }
                if (idx < 0) idx = n;
                int chunk = idx - offset;
                if (chunk > 0)
                {
                    if (currentFileName != null)
                    {
                        currentData.Write(buffer, offset, chunk);
                    }
                    else
                    {
                        byte[] slice = new byte[chunk];
                        Array.Copy(buffer, offset, slice, 0, chunk);
                        int headerEndIdx = IndexOfBytes(slice, headerEndCrlf);
                        int bodyStartDelta = 4;
                        if (headerEndIdx < 0)
                        {
                            headerEndIdx = IndexOfBytes(slice, headerEndLf);
                            bodyStartDelta = 2;
                        }
                        if (headerEndIdx >= 0)
                        {
                            var headers = Encoding.UTF8.GetString(slice, 0, headerEndIdx);
                            currentName = GetHeaderValue(headers, "name");
                            currentFileName = GetFileNameFromContentDisposition(headers);
                            int bodyStart = headerEndIdx + bodyStartDelta;
                            if (bodyStart < chunk && currentFileName != null)
                                currentData.Write(buffer, offset + bodyStart, chunk - bodyStart);
                        }
                    }
                }
                if (idx >= n) break;
                // when we hit a boundary we always reset buffers; if there was a file part we add it first
                if (currentFileName != null && currentData.Length > 0)
                {
                    parts.Add(new Part { FileName = currentFileName, Name = currentName ?? "", Data = currentData.ToArray() });
                }
                // clear state regardless of whether the previous part was a file
                currentData.SetLength(0);
                currentData.Position = 0;
                currentFileName = null;
                currentName = null;
                offset = idx + skipLen;
                bool isEnd = (offset + endCrlf.Length <= n && Match(buffer, offset, endCrlf)) ||
                             (offset + endLf.Length <= n && Match(buffer, offset, endLf));
                if (isEnd) break;
            }
        }
        if (currentFileName != null && currentData.Length > 0)
            parts.Add(new Part { FileName = currentFileName, Name = currentName ?? "", Data = currentData.ToArray() });

        return parts;
    }

    private static int IndexOf(byte[] buffer, byte[] pattern, int start, int count)
    {
        for (int i = start; i <= count - pattern.Length; i++)
        {
            if (Match(buffer, i, pattern)) return i;
        }
        return -1;
    }

    private static int IndexOfBytes(byte[] buffer, byte[] pattern)
    {
        for (int i = 0; i <= buffer.Length - pattern.Length; i++)
        {
            if (Match(buffer, i, pattern)) return i;
        }
        return -1;
    }

    private static bool Match(byte[] buffer, int start, byte[] pattern)
    {
        for (int i = 0; i < pattern.Length; i++)
            if (buffer[start + i] != pattern[i]) return false;
        return true;
    }

    private static string GetHeaderValue(string headers, string name)
    {
        var lines = headers.Split("\r\n");
        foreach (var line in lines)
        {
            if (line.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
                return line[(name.Length + 1)..].Trim(' ', '"');
        }
        return "";
    }

    private static string? GetFileNameFromContentDisposition(string headers)
    {
        var lines = headers.Split("\r\n");
        foreach (var line in lines)
        {
            if (line.StartsWith("Content-Disposition:", StringComparison.OrdinalIgnoreCase))
            {
                int idx = line.IndexOf("filename=", StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    var v = line[(idx + 9)..].Trim();
                    if (v.StartsWith('"')) v = v.Trim('"');
                    return v.Trim();
                }
            }
        }
        return null;
    }

    private static string SanitizeFileName(string name)
    {
        name = Path.GetFileName(name);
        if (string.IsNullOrEmpty(name)) return "";
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (char c in name)
            sb.Append(invalid.Contains(c) ? '_' : c);
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

    private static async Task SendJsonAsync(HttpListenerResponse response, int statusCode, object obj)
    {
        response.StatusCode = statusCode;
        response.ContentType = "application/json";
        var json = JsonSerializer.Serialize(obj, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        response.Close();
    }

    private void Log(string msg)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {msg}";
        LogMessage?.Invoke(this, line);
    }

    public void Dispose() => Stop();
}

public class ReceivedFileEventArgs : EventArgs
{
    public string FileName { get; }
    public string FullPath { get; }
    public long SizeBytes { get; }
    public string Source { get; }
    public ReceivedFileEventArgs(string fileName, string fullPath, long sizeBytes, string source)
    {
        FileName = fileName; FullPath = fullPath; SizeBytes = sizeBytes; Source = source;
    }
}

internal class Part
{
    public string FileName { get; set; } = "";
    public string Name { get; set; } = "";
    public byte[] Data { get; set; } = Array.Empty<byte>();
}
