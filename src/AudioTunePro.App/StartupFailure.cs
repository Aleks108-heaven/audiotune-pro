using System.IO;
using System.Windows;

namespace AudioTunePro.App;

/// <summary>
/// Reports a failure that happens while the app is starting. Deliberately uses no AudioTunePro.Core types:
/// if Windows (Smart App Control / an Application Control policy) blocks AudioTunePro.Core.dll, the app has
/// to explain that without that library, instead of vanishing as an unhandled exception.
/// </summary>
internal static class StartupFailure
{
    private const int ErrorApplicationControlBlocked = unchecked((int)0x800711C7);

    public static string LogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AudioTunePro", "logs");

    public static void Report(Exception ex)
    {
        var blocked = IsBlockedByApplicationControl(ex);
        TryWriteLog(ex);

        var text = blocked
            ? "Windows blocked a file AudioTune Pro needs (Smart App Control or an Application Control policy).\n\n" +
              "This build is not code-signed, so Windows can refuse to run it. Install a signed release, or ask your " +
              "administrator to allow AudioTune Pro.\n\nDetails were saved to:\n" + LogDirectory
            : "AudioTune Pro could not start.\n\n" + ex.GetType().Name + ": " + ex.Message +
              "\n\nDetails were saved to:\n" + LogDirectory;

        try
        {
            System.Windows.MessageBox.Show(text, "AudioTune Pro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception)
        {
            // No UI available either; the log file above is all that can be done.
        }
    }

    private static bool IsBlockedByApplicationControl(Exception? ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e.HResult == ErrorApplicationControlBlocked ||
                e.Message.Contains("Application Control policy", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static void TryWriteLog(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            File.AppendAllText(Path.Combine(LogDirectory, "startup-error.log"),
                $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z startup failed{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // Best effort.
        }
    }
}
