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
            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html><head><title>ClipShelf</title>");
            sb.Append("<style>body{font-family:Arial;background:#f0f0f0;padding:20px;}");
            sb.Append(".container{max-width:600px;margin:0 auto;background:white;padding:30px;}");
            sb.Append("h1{color:#2E75B6;text-align:center;}");
            sb.Append("input{width:100%;padding:10px;margin:10px 0;}");
            sb.Append("button{width:100%;padding:12px;background:#2E75B6;color:white;border:none;cursor:pointer;}");
            sb.Append(".file-item{background:#f9f9f9;padding:10px;margin:5px 0;}</style></head><body>");
            sb.Append("<div class=\"container\"><h1>ClipShelf Phone Sync</h1>");
            sb.Append("<input type=\"password\" id=\"pin\" placeholder=\"Enter PIN\"><br>");
            sb.Append("<input type=\"file\" id=\"files\" multiple><br>");
            sb.Append("<button onclick=\"upload()\">Upload</button>");
            sb.Append("<div id=\"list\"></div></div>");
            sb.Append("<script>function upload(){const p=document.getElementById('pin').value;");
            sb.Append("const f=document.getElementById('files').files;const d=new FormData();");
            sb.Append("d.append('pin',p);for(let i=0;i<f.length;i++)d.append('files',f[i]);");
            sb.Append("fetch('/upload',{method:'POST',body:d}).then(r=>r.json()).then(e=>{");
            sb.Append("if(e.success)alert('Success');else alert(e.message)});}");
            sb.Append("fetch('/files').then(r=>r.json()).then(f=>{");
            sb.Append("document.getElementById('list').innerHTML=f.map(x=>'<div class=\"file-item\">'+x+'</div>').join('');</script></body></html>");

            string html = sb.ToString();
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

                int uploadedCount = 0;
                for (int i = 0; i < context.Request.Files.Count; i++)
                {
                    var file = context.Request.Files[i];
                    if (file.ContentLength > 104857600) continue;

                    string filename = Path.GetFileName(file.FileName);
                    string filepath = Path.Combine(_uploadFolder, filename);
                    file.SaveAs(filepath);
                    uploadedCount++;

                    UploadedFiles.Insert(0, filename);
                    FileUploadedEvent?.Invoke(this, filename);
                }

                SendJson(context, new { success = uploadedCount > 0, message = uploadedCount + " file(s) uploaded" });
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
