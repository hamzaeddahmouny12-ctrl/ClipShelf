using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace ClipShelf.Services
{
    public class PhoneSyncServer
    {
        private HttpListener _httpListener;
        private bool _isRunning;
        private readonly int _port;
        private readonly string _pin;
        private readonly string _uploadFolder;
        public ObservableCollection<string> UploadedFiles { get; }

        public event EventHandler<string> FileUploadedEvent;
        public event EventHandler<string> ServerStatusChanged;

        public PhoneSyncServer(int port, string pin)
        {
            _port = port;
            _pin = pin;
            _uploadFolder = Path.Combine(Path.GetTempPath(), "ClipShelf_Uploads");
            Directory.CreateDirectory(_uploadFolder);
            UploadedFiles = new ObservableCollection<string>();
        }

        public async Task StartAsync()
        {
            if (_isRunning) return;

            _httpListener = new HttpListener();
            _httpListener.Prefixes.Add($"http://+:{_port}/");

            try
            {
                _httpListener.Start();
                _isRunning = true;
                ServerStatusChanged?.Invoke(this, "Server started");

                while (_isRunning)
                {
                    try
                    {
                        HttpListenerContext context = await _httpListener.GetContextAsync();
                        ProcessRequest(context);
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                ServerStatusChanged?.Invoke(this, $"Error: {ex.Message}");
            }
        }

        private void ProcessRequest(HttpListenerContext context)
        {
            string path = context.Request.Url.AbsolutePath;

            if (path == "/" && context.Request.HttpMethod == "GET")
            {
                SendWebInterface(context);
            }
            else if (path == "/upload" && context.Request.HttpMethod == "POST")
            {
                HandleFileUpload(context);
            }
            else if (path == "/files" && context.Request.HttpMethod == "GET")
            {
                SendFileList(context);
            }
            else
            {
                context.Response.StatusCode = 404;
                context.Response.Close();
            }
        }

        private void SendWebInterface(HttpListenerContext context)
        {
            string html = @"
<!DOCTYPE html>
<html>
<head>
    <title>ClipShelf Phone Sync</title>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1'>
    <style>
        * { margin: 0; padding: 0; box-sizing: border-box; }
        body { font-family: Arial, sans-serif; background: #f0f0f0; padding: 20px; }
        .container { max-width: 600px; margin: 0 auto; background: white; border-radius: 10px; padding: 30px; box-shadow: 0 2px 10px rgba(0,0,0,0.1); }
        h1 { color: #2E75B6; margin-bottom: 20px; text-align: center; }
        .form-group { margin-bottom: 20px; }
        label { display: block; margin-bottom: 5px; color: #333; font-weight: bold; }
        input[type='password'], input[type='text'] { width: 100%; padding: 10px; border: 1px solid #ddd; border-radius: 5px; font-size: 14px; }
        .upload-area { border: 2px dashed #2E75B6; border-radius: 5px; padding: 30px; text-align: center; color: #666; cursor: pointer; transition: all 0.3s; }
        .upload-area:hover { background: #f0f7ff; }
        .upload-area.active { background: #e3f2fd; border-color: #1565c0; }
        button { width: 100%; padding: 12px; background: #2E75B6; color: white; border: none; border-radius: 5px; font-size: 16px; font-weight: bold; cursor: pointer; transition: background 0.3s; }
        button:hover { background: #1565c0; }
        button:disabled { background: #999; cursor: not-allowed; }
        .message { padding: 10px; margin-top: 10px; border-radius: 5px; display: none; }
        .success { background: #d4edda; color: #155724; display: block; }
        .error { background: #f8d7da; color: #721c24; display: block; }
        .file-list { margin-top: 30px; }
        .file-item { background: #f9f9f9; padding: 10px; margin: 5px 0; border-radius: 5px; display: flex; justify-content: space-between; align-items: center; }
        #fileInput { display: none; }
    </style>
</head>
<body>
    <div class='container'>
        <h1>🕐 ClipShelf - Phone Sync</h1>
        
        <div class='form-group'>
            <label>Authentication PIN</label>
            <input type='password' id='pin' placeholder='Enter PIN' required>
        </div>

        <div class='form-group'>
            <label>Upload Files (Drag & Drop or Click)</label>
            <div class='upload-area' id='uploadArea' onclick='document.getElementById(\"fileInput\").click()'>
                <p>Drag files here or click to select</p>
                <p style='font-size: 12px; margin-top: 10px; color: #999;'>Max 100MB per file</p>
            </div>
            <input type='file' id='fileInput' multiple>
        </div>

        <button onclick='uploadFiles()'>Upload Files</button>

        <div id='message' class='message'></div>

        <div class='file-list' id='fileList' style='display: none;'>
            <h3>Received Files</h3>
            <div id='files'></div>
        </div>
    </div>

    <script>
        const uploadArea = document.getElementById('uploadArea');
        const fileInput = document.getElementById('fileInput');

        uploadArea.addEventListener('dragover', (e) => {
            e.preventDefault();
            uploadArea.classList.add('active');
        });

        uploadArea.addEventListener('dragleave', () => {
            uploadArea.classList.remove('active');
        });

        uploadArea.addEventListener('drop', (e) => {
            e.preventDefault();
            uploadArea.classList.remove('active');
            fileInput.files = e.dataTransfer.files;
        });

        function uploadFiles() {
            const pin = document.getElementById('pin').value;
            if (!pin) {
                showMessage('Please enter PIN', false);
                return;
            }

            const files = fileInput.files;
            if (files.length === 0) {
                showMessage('Please select files', false);
                return;
            }

            for (let file of files) {
                if (file.size > 104857600) { // 100MB
                    showMessage('File too large: ' + file.name, false);
                    return;
                }
            }

            const formData = new FormData();
            formData.append('pin', pin);
            for (let file of files) {
                formData.append('files', file);
            }

            fetch('/upload', {
                method: 'POST',
                body: formData
            })
            .then(r => r.json())
            .then(data => {
                if (data.success) {
                    showMessage('Files uploaded successfully!', true);
                    fileInput.value = '';
                    loadFiles();
                } else {
                    showMessage(data.message || 'Upload failed', false);
                }
            })
            .catch(e => showMessage('Error: ' + e.message, false));
        }

        function showMessage(msg, success) {
            const msgDiv = document.getElementById('message');
            msgDiv.textContent = msg;
            msgDiv.className = 'message ' + (success ? 'success' : 'error');
        }

        function loadFiles() {
            fetch('/files')
            .then(r => r.json())
            .then(files => {
                if (files.length > 0) {
                    document.getElementById('fileList').style.display = 'block';
                    document.getElementById('files').innerHTML = files.map(f => 
                        '<div class=\"file-item\"><span>' + f + '</span></div>'
                    ).join('');
                }
            });
        }

        loadFiles();
    </script>
</body>
</html>";

            byte[] buffer = Encoding.UTF8.GetBytes(html);
            context.Response.ContentType = "text/html";
            context.Response.ContentLength64 = buffer.Length;
            context.Response.OutputStream.Write(buffer, 0, buffer.Length);
            context.Response.Close();
        }

        private void HandleFileUpload(HttpListenerContext context)
        {
            try
            {
                string pin = context.Request.QueryString["pin"];
                if (pin != _pin)
                {
                    SendJson(context, new { success = false, message = "Invalid PIN" }, 401);
                    return;
                }

                var files = context.Request.Files;
                int uploadedCount = 0;

                for (int i = 0; i < context.Request.Files.Count; i++)
                {
                    var file = context.Request.Files[i];
                    if (file.ContentLength > 104857600) continue; // 100MB

                    string filename = Path.GetFileName(file.FileName);
                    string filepath = Path.Combine(_uploadFolder, filename);
                    
                    file.SaveAs(filepath);
                    uploadedCount++;
                    
                    UploadedFiles.Insert(0, filename);
                    FileUploadedEvent?.Invoke(this, filename);
                }

                SendJson(context, new { success = uploadedCount > 0, message = $"{uploadedCount} file(s) uploaded" });
            }
            catch (Exception ex)
            {
                SendJson(context, new { success = false, message = ex.Message }, 500);
            }
        }

        private void SendFileList(HttpListenerContext context)
        {
            var files = Directory.GetFiles(_uploadFolder).Select(f => Path.GetFileName(f)).ToArray();
            SendJson(context, files);
        }

        private void SendJson(HttpListenerContext context, object data, int statusCode = 200)
        {
            string json = System.Text.Json.JsonSerializer.Serialize(data);
            byte[] buffer = Encoding.UTF8.GetBytes(json);
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = buffer.Length;
            context.Response.OutputStream.Write(buffer, 0, buffer.Length);
            context.Response.Close();
        }

        public void Stop()
        {
            _isRunning = false;
            _httpListener?.Stop();
            _httpListener?.Close();
        }
    }
}
