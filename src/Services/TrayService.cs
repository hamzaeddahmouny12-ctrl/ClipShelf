using System.Windows.Forms;

namespace ClipShelf.Services;

/// <summary>System tray icon with menu: Open Main Window, Phone Sync, Exit.</summary>
public class TrayService : IDisposable
{
    private NotifyIcon? _notifyIcon;
    private readonly Action _onOpenMain;
    private readonly Action _onOpenPhoneSync;
    private readonly Action _onOpenShutdownTimer;
    private readonly Action _onExit;

    public TrayService(Action onOpenMain, Action onOpenPhoneSync, Action onOpenShutdownTimer, Action onExit)
    {
        _onOpenMain = onOpenMain;
        _onOpenPhoneSync = onOpenPhoneSync;
        _onOpenShutdownTimer = onOpenShutdownTimer;
        _onExit = onExit;
    }

    public void Show()
    {
        if (_notifyIcon != null) return;

        _notifyIcon = new NotifyIcon
        {
            Text = "ClipShelf",
            Icon = SystemIcons.Application,
            Visible = true
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open ClipShelf", null, (_, _) => _onOpenMain());
        menu.Items.Add("Phone Sync", null, (_, _) => _onOpenPhoneSync());
        menu.Items.Add("Shutdown Timer", null, (_, _) => _onOpenShutdownTimer());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => _onExit());
        _notifyIcon.ContextMenuStrip = menu;

        _notifyIcon.DoubleClick += (_, _) => _onOpenMain();
    }

    public void Hide()
    {
        if (_notifyIcon != null) _notifyIcon.Visible = false;
    }

    public void Dispose()
    {
        _notifyIcon?.Dispose();
        _notifyIcon = null;
    }
}
