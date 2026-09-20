using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using AudioTunePro.App.Services;
using AudioTunePro.App.ViewModels;

namespace AudioTunePro.App;

/// <summary>Interaction logic for MainWindow.xaml</summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _isExiting;
    private bool _isLoaded;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        // WindowChrome (WindowStyle="None") can fire a spurious StateChanged(Minimized)
        // during startup layout, before the window has ever been shown — ignore state
        // changes until Loaded so that doesn't hide the window before the user sees it.
        Loaded += (_, _) => _isLoaded = true;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        TryEnableDarkTitleBar();
    }

    private void TryEnableDarkTitleBar()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int useDarkMode = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDarkMode, sizeof(int));
        }
        catch (EntryPointNotFoundException)
        {
            // Older Windows build without this DWM attribute — cosmetic only, safe to ignore.
        }
    }

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    private void DownloadApo_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(EqualizerApoInstallService.DownloadUrl) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            System.Windows.MessageBox.Show(this,
                $"Open this link in your browser:\n{EqualizerApoInstallService.DownloadUrl}",
                "AudioTune Pro", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void SavePreset_Click(object sender, RoutedEventArgs e)
    {
        var name = NewPresetNameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name)) return;
        _viewModel.SaveAsNewPreset(name);
    }

    private void DeletePreset_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedPreset is null || _viewModel.SelectedPreset.IsBuiltIn) return;

        var result = System.Windows.MessageBox.Show(this,
            $"Delete preset \"{_viewModel.SelectedPreset.Name}\"?",
            "AudioTune Pro", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes) _viewModel.DeleteSelectedPreset();
    }

    private void Reset_Click(object sender, RoutedEventArgs e) => _viewModel.ResetCurrentPreset();

    // --- Custom TitleBar chrome (WindowStyle="None" gives up the native title bar entirely) ---

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return;
        }

        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (_isLoaded && WindowState == WindowState.Minimized)
        {
            Hide();
            return;
        }

        var maximized = WindowState == WindowState.Maximized;
        MaximizeIcon.Visibility = maximized ? Visibility.Collapsed : Visibility.Visible;
        RestoreIcon.Visibility = maximized ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Closing the window keeps AudioTune Pro running in the tray so the EQ
        // stays applied; only the tray "Exit" command truly quits the app.
        if (_isExiting) return;
        e.Cancel = true;
        Hide();
    }

    internal void ForceClose()
    {
        _isExiting = true;
        _viewModel.Dispose();
        Close();
    }
}
