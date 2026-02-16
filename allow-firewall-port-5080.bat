@echo off
:: Run this once as Administrator if your phone cannot reach ClipShelf (same WiFi).
:: Right-click -> Run as administrator
netsh advfirewall firewall add rule name="ClipShelf Phone Sync" dir=in action=allow protocol=TCP localport=5080
if %errorlevel% equ 0 (
    echo Firewall rule added. You can now open the QR link on your phone.
) else (
    echo Failed. Make sure you ran this as Administrator.
)
pause
