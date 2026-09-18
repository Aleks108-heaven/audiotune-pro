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

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
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

    private const double CeilingStepDb = 0.1;

    private void CeilingDown_Click(object sender, RoutedEventArgs e) =>
        _viewModel.LimiterCeilingDb = Math.Round(_viewModel.LimiterCeilingDb - CeilingStepDb, 1);

    private void CeilingUp_Click(object sender, RoutedEventArgs e) =>
        _viewModel.LimiterCeilingDb = Math.Round(_viewModel.LimiterCeilingDb + CeilingStepDb, 1);

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized) Hide();
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
