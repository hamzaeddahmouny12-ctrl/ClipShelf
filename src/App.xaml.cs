using System.Windows;
using ClipShelf.Services;
using ClipShelf.Views;

namespace ClipShelf;

public partial class App : System.Windows.Application
{
    private TrayService? _tray;
    private Views.ShutdownTimerWindow? _shutdownWindow;
    private MainWindow? _mainWindow;
    private PhoneSyncWindow? _phoneSyncWindow;

    public BackgroundAgent? BackgroundAgent { get; private set; }

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        BackgroundAgent = new BackgroundAgent(BackgroundAgent.DefaultWebPort);
        BackgroundAgent.Start();
        // ensure a desktop shortcut exists pointing to this exe
        CreateDesktopShortcut();

        _tray = new TrayService(OpenMainWindow, OpenPhoneSync, OpenShutdownTimer, ExitApp);
        _tray.Show();

        OpenMainWindow();
    }

    private void OpenMainWindow()
    {
        if (_mainWindow == null || !_mainWindow.IsLoaded)
        {
            _mainWindow = new MainWindow();
            _mainWindow.Closed += (_, _) => _mainWindow = null;
            _mainWindow.Show();
        }
        else
        {
            _mainWindow.Activate();
        }
    }

    public void OpenPhoneSync()
    {
        if (_phoneSyncWindow == null || !_phoneSyncWindow.IsLoaded)
        {
            _phoneSyncWindow = new PhoneSyncWindow(BackgroundAgent);
            _phoneSyncWindow.Closed += (_, _) => _phoneSyncWindow = null;
            _phoneSyncWindow.Show();
        }
        else
        {
            _phoneSyncWindow.Activate();
        }
    }

    public void OpenShutdownTimer()
    {
        if (_shutdownWindow == null || !_shutdownWindow.IsLoaded)
        {
            _shutdownWindow = new Views.ShutdownTimerWindow();
            _shutdownWindow.Closed += (_, _) => _shutdownWindow = null;
            _shutdownWindow.Show();
        }
        else
        {
            _shutdownWindow.Activate();
        }
    }

    public void ExitApp()
    {
        _tray?.Dispose();
        BackgroundAgent?.Dispose();
        Current.Shutdown();
    }

    private void CreateDesktopShortcut()
    {
        try
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string linkPath = System.IO.Path.Combine(desktop, "ClipShelf.lnk");
            if (System.IO.File.Exists(linkPath)) return;

            // use WSH to create shortcut
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            dynamic shell = Activator.CreateInstance(t);
            var shortcut = shell.CreateShortcut(linkPath);
            shortcut.TargetPath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
            shortcut.WorkingDirectory = System.IO.Path.GetDirectoryName(shortcut.TargetPath);
            shortcut.WindowStyle = 1;
            shortcut.Save();
        }
        catch { }
    }
}
