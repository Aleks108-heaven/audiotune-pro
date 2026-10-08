using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        // Never open taller or wider than the screen work area (small laptops at high DPI). The minimum
        // size is clamped too: a MinHeight/MinWidth above the work area would override this and push the
        // footer or edges off-screen. The sidebar scrolls and the faders shrink, so the smaller window works.
        var work = SystemParameters.WorkArea;
        MinHeight = Math.Min(MinHeight, work.Height - 16);
        MinWidth = Math.Min(MinWidth, work.Width - 16);
        Height = Math.Min(Height, work.Height - 16);
        Width = Math.Min(Width, work.Width - 16);
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
        catch (DllNotFoundException)
        {
            // Same: no DWM available, nothing to do.
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

    private void SavePreset_Click(object sender, RoutedEventArgs e) =>
        _viewModel.SaveAsNewPreset(NewPresetNameBox.Text);

    private void NewPresetNameBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        _viewModel.SaveAsNewPreset(NewPresetNameBox.Text);
        e.Handled = true;
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

    /// <summary>Double-click on a fader snaps it back to 0 dB (the pro-audio convention).</summary>
    private void Fader_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || sender is not Slider { DataContext: BandViewModel band }) return;
        band.GainDb = 0;
        e.Handled = true;
    }

    /// <summary>Keyboard equivalent of the double-click reset: Delete sets the focused fader to 0 dB (Home/End jump to the extremes).</summary>
    private void Fader_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Delete || sender is not Slider { DataContext: BandViewModel band }) return;
        band.GainDb = 0;
        e.Handled = true;
    }

    // --- Custom TitleBar chrome (WindowStyle="None" gives up the native title bar entirely) ---

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        var maximized = WindowState == WindowState.Maximized;
        MaximizeIcon.Visibility = maximized ? Visibility.Collapsed : Visibility.Visible;
        RestoreIcon.Visibility = maximized ? Visibility.Visible : Visibility.Collapsed;
        // A borderless WindowChrome window maximizes with its invisible resize frame hanging past the
        // screen edges; pull the content back inside so nothing is clipped off-screen.
        RootGrid.Margin = maximized ? SystemParameters.WindowResizeBorderThickness : new Thickness(0);
        UpdateMeterActivity();
    }

    private void Window_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) => UpdateMeterActivity();

    /// <summary>The level meter only needs to run while the window is on screen (not hidden to the tray or minimized).</summary>
    private void UpdateMeterActivity() =>
        _viewModel.SetWindowActive(IsVisible && WindowState != WindowState.Minimized);

    /// <summary>Picks up an Equalizer APO install the user just finished, so the banner clears and the EQ is applied.</summary>
    private void Window_Activated(object? sender, EventArgs e) => _viewModel.RefreshInstallStatus();

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Closing the window keeps AudioTune Pro running in the tray so the EQ
        // stays applied; only the tray "Exit" command truly quits the app.
        if (_isExiting) return;
        e.Cancel = true;
        Hide();

        if (_viewModel.ConsumeTrayHint())
            (System.Windows.Application.Current as App)?.ShowTrayHint();
    }

    /// <summary>Lets the window close during logoff/shutdown instead of hiding to the tray.</summary>
    internal void PrepareForSessionEnd()
    {
        _isExiting = true;
        _viewModel.Dispose();
    }

    internal void ForceClose()
    {
        _isExiting = true;
        _viewModel.Dispose();
        Close();
    }
}
