using System.IO;
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
    private MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

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
