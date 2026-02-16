# ClipShelf – Background Agent & Phone Sync

This document describes the **Background Agent**, **Phone Sync**, and the new **Shutdown Timer** added to ClipShelf.

---

## 1. Overview

- **Background Agent**: A persistent service that runs even when the main window is closed. It stays in the system tray and keeps the web server and (optionally) Bluetooth listener running.
- **Phone Sync**: A way to send files from your phone to the PC over the same Wi‑Fi (web upload) or via Bluetooth.  New in this version, the PC can also share files back to the phone using the same web link.
- **Shutdown Timer**: Schedule the PC to shut down after a specified number of seconds using the button next to Phone Sync (or via tray menu). You can also cancel a pending shutdown from the same dialog.
- **Search bar**: At the top of the main window you can now type a query and press Enter (or click **Search PC**) to perform a system‑wide file search using Windows Explorer.

---

## 2. Background Agent

**Role**

- Runs as long as the app is running (including when the main window is closed).
- Listens for incoming connections (HTTP and optionally Bluetooth).
- Handles file uploads and stores them in a single folder.
- Writes a log of received files and important events.
- Restarts the web server automatically if it crashes (after a short delay).
- Exposes status and events to the UI (e.g. Phone Sync window).

**Behaviour**

- Started from `App.Application_Startup` when the app starts.
- Stops when the user chooses **Exit** from the tray menu (or the app shuts down).
- Log file: `%LocalAppData%\ClipShelf\PhoneSync\sync_log.txt`.
- Received files are saved under: `%LocalAppData%\ClipShelf\PhoneSync\`.
- Files the PC shares for download are placed in `%LocalAppData%\ClipShelf\PhoneSync\Outgoing\` (via the "Share files" button); the phone can download them from the same QR link.

---

## 3. Phone Sync Window

**How to open**

- Tray icon → **Phone Sync**.
- Or from the main window: **Phone Sync** button.

**Contents**

- **QR code**: Encodes `http://<PC-IP>:<PORT>?pin=<PIN>` so the phone can open the upload/download page.
- **Status**: Connected / Disconnected (web server).
- **Local IP** and **PIN**: Shown so you can type them on the phone if needed.
- **Bluetooth**: Status (e.g. Listening / Unavailable).
- **Regenerate PIN**: New 6‑digit PIN; previous PIN stops working; the QR code updates.
- **Open folder**: Opens the folder where received files are stored.
- **Received files**: List with file name, time, and source (Web or Bluetooth).

**PIN rules**

- New PIN each time you click **Regenerate PIN**.
- PIN expires **10 minutes** after it was generated if it has never been used for a successful auth/upload.
- Only the current PIN is accepted.

---

## 4. Local Web Server (PC)

**Technology**

- Lightweight HTTP server using `HttpListener` (no ASP.NET).
- Port: **5080** (configurable via `BackgroundAgent.DefaultWebPort`).

**Endpoints**

| Endpoint    | Method | Description |
|------------|--------|-------------|
| `/ping`    | GET    | Health check; returns `{ ok, timestamp }`. |
| `/status`  | GET    | Server status; indicates if the PIN is expired. |
| `/auth`    | POST   | Body: JSON `{ "pin": "123456" }`. Returns success/failure. |
| `/upload`  | POST   | Multipart form data; requires valid PIN (header `X-Pin` or query `?pin=`). |

**Upload rules**

- **Authentication**: PIN in `X-Pin` header or `pin` query parameter. Must match current PIN and not be expired.
- **Size**: Single request body limited to **100 MB**.
- **Network**: Only requests from **local network** IPs (e.g. 192.168.x.x, 10.x.x.x) are accepted; others get 403.
- **Files**: Filenames are sanitized (invalid characters replaced). Duplicate names get a numeric suffix.
- **Storage**: Same folder as the rest of Phone Sync: `%LocalAppData%\ClipShelf\PhoneSync\`.

When a file is successfully stored, the server notifies the Background Agent, which updates the Phone Sync window and the log.

**If your phone says "This site can't be reached" (same WiFi as PC):**
1. The app binds to your PC’s local IP (e.g. 192.168.1.x) so the QR code and URL use that address.
2. **Windows Firewall** often blocks incoming connections. Do one of the following:
   - Run **allow-firewall-port-5080.bat** as Administrator (right‑click → Run as administrator) once. This adds a rule to allow TCP port 5080.
   - Or: Windows Security → Firewall → Allow an app → find ClipShelf and allow **Private** networks.
   - Or: Windows Defender Firewall → Advanced settings → Inbound Rules → New Rule → Port → TCP 5080 → Allow.
3. On the phone, open the URL shown in the QR (e.g. `http://192.168.1.10:5080?pin=123456`). Use the same IP as in the Phone Sync window. The page now includes both an upload area and a list of any files the PC has shared; tapping a filename downloads it.

---

## 5. Bluetooth Support

**Direction**: Phone → PC (phone sends files to the PC).

**Implementation**

- Uses **32feet.NET** (OBEX) to listen for incoming Bluetooth file transfers.
- If Bluetooth is not supported or the library fails, the feature is disabled and the rest of the app (including web sync) still works.
- Received files are saved in the **same folder** as web uploads and appear in the Phone Sync window with source “Bluetooth”.
- Status is shown in the Phone Sync window (e.g. Listening / Unavailable).

**Note**

- 32feet.NET is built for .NET Framework; it is used from this .NET 6 app with a compatibility warning. Bluetooth is optional; if you see issues, you can remove the package and the `BluetoothSyncService` usage.

---

## 6. Security

- **PIN**: Rotated when you click **Regenerate PIN**; expires after 10 minutes if unused.
- **Local network only**: Web server rejects non‑local IPs (403).
- **Filenames**: Sanitized to avoid path traversal and invalid characters.
- **Upload size**: Capped at 100 MB per request.
- **Logging**: Connection and upload events are written to the sync log.

---

## 7. File Layout (code)

| Component            | File(s) |
|---------------------|--------|
| Background agent    | `Services/BackgroundAgent.cs` |
| Web server          | `Services/SyncWebServer.cs` |
| Bluetooth listener  | `Services/BluetoothSyncService.cs` |
| QR code             | `Services/QRCodeService.cs` |
| Tray icon           | `Services/TrayService.cs` |
| Phone Sync UI       | `Views/PhoneSyncWindow.xaml`, `Views/PhoneSyncWindow.xaml.cs` |
| Phone Sync logic    | `ViewModels/PhoneSyncViewModel.cs` |
| Received file model | `Models/ReceivedFileItem.cs` |
| App lifecycle       | `App.xaml.cs` (startup, tray, agent, Phone Sync) |

---

## 8. User Flow (phone → PC)

1. On the PC: open **Phone Sync** (tray or main window).
2. On the phone: open the camera or a QR app and scan the QR code (or open `http://<PC-IP>:5080?pin=<PIN>` in the browser).
3. Enter the PIN shown in the Phone Sync window (or leave it in the URL).
4. Use the web page to select or drag‑and‑drop files (max 100 MB per file).
5. Files are saved on the PC and appear in the “Received files” list and in the folder opened by **Open folder**.

The app can stay in the tray with the main window closed; the Background Agent keeps the server and Bluetooth listener running until you exit the app.
