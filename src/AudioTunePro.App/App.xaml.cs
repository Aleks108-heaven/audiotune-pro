using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Forms;
using AudioTunePro.App.Assets;
using Application = System.Windows.Application;
using MenuItem = System.Windows.Forms.ToolStripMenuItem;

namespace AudioTunePro.App;

/// <summary>
/// Interaction logic for App.xaml. Owns the tray icon so AudioTune Pro can keep
/// applying its EQ in the background (low CPU: it's Equalizer APO doing the DSP
/// work, this process just edits config files) after the window is closed.
/// </summary>
public partial class App : Application
{
    private NotifyIcon? _trayIcon;
    private Mutex? _singleInstance;
    private EventWaitHandle? _showSignal;
    private MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // One instance only: two copies would fight over the same Equalizer APO config file.
        _singleInstance = new Mutex(true, @"Local\AudioTunePro.SingleInstance", out var isFirst);
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\AudioTunePro.ShowWindow");
        if (!isFirst)
        {
            if (!e.Args.Contains("--minimized")) _showSignal.Set(); // ask the running copy to show itself
            Shutdown();
            return;
        }
        var signal = _showSignal;
        new Thread(() =>
        {
            while (signal.WaitOne()) Dispatcher.BeginInvoke(() => ShowMainWindow());
        }) { IsBackground = true, Name = "ShowWindowSignal" }.Start();

        Views.HighContrastTheme.ApplyIfActive(Resources);

        var icon = LoadAppIcon();

        _trayIcon = new NotifyIcon
        {
            Icon = icon,
            Text = "AudioTune Pro",
            Visible = true,
        };
        _trayIcon.DoubleClick += (_, _) => ShowMainWindow();

        var menu = new ContextMenuStrip();
        menu.Items.Add(new MenuItem("Open AudioTune Pro", null, (_, _) => ShowMainWindow()));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(new MenuItem("Exit", null, (_, _) => ExitApplication()));
        _trayIcon.ContextMenuStrip = menu;

        _mainWindow = new MainWindow();
        _mainWindow.Closed += (_, _) => _mainWindow = null;

        bool startMinimized = e.Args.Contains("--minimized");
        if (!startMinimized)
        {
            _mainWindow.Show();
        }
    }

    /// <summary>
    /// Windows is logging off or shutting down. The main window normally cancels Closing to hide to the
    /// tray, which would make Windows report this app as blocking shutdown; let it close for real
    /// (this also flushes any pending EQ change) and release the tray icon.
    /// </summary>
    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        base.OnSessionEnding(e);
        _mainWindow?.PrepareForSessionEnd();
        _trayIcon?.Dispose();
    }

    /// <summary>One-time balloon so closing the window doesn't look like the app (and its EQ) quit.</summary>
    internal void ShowTrayHint() =>
        _trayIcon?.ShowBalloonTip(5000, "AudioTune Pro is still running",
            "Your equalizer stays active in the tray. Right-click the tray icon and choose Exit to quit.",
            System.Windows.Forms.ToolTipIcon.Info);

    private void ShowMainWindow()
    {
        if (_mainWindow is null)
        {
            _mainWindow = new MainWindow();
            _mainWindow.Closed += (_, _) => _mainWindow = null;
        }

        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    private void ExitApplication()
    {
        _mainWindow?.ForceClose();
        _trayIcon?.Dispose();
        _singleInstance?.Dispose();
        Shutdown();
    }

    /// <summary>
    /// Uses the icon baked into this EXE (set via ApplicationIcon in the .csproj) so the
    /// tray icon always matches the taskbar/desktop/Start-Menu icon. Falls back to the
    /// runtime-drawn icon if extraction ever fails (e.g. a stripped/unusual build).
    /// </summary>
    private static System.Drawing.Icon LoadAppIcon()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (exePath is not null)
            {
                var extracted = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                if (extracted is not null) return extracted;
            }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        {
            // Fall through to the generated fallback below.
        }

        return IconFactory.CreateAppIcon();
    }
}
