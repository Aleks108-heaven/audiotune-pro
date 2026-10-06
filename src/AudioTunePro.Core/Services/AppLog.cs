using System.Text;

namespace AudioTunePro.Core.Services;

/// <summary>
/// Small size-capped diagnostic log (%AppData%\AudioTunePro\logs\app.log). The app had no logging at all, so
/// crashes and swallowed errors left nothing to investigate. Writing never throws: a full disk or a locked
/// file must not take the app down.
/// </summary>
public sealed class AppLog
{
    /// <summary>When the file passes this size it is moved to app.log.1 (replacing the previous one).</summary>
    public const long MaxBytes = 256 * 1024;

    private readonly object _gate = new();
    private readonly string _path;

    public AppLog(string directory) => _path = Path.Combine(directory, "app.log");

    public string FilePath => _path;

    /// <summary>The log the whole app writes to; null (logging off) until the app sets it.</summary>
    public static AppLog? Current { get; set; }

    public static void Info(string message) => Current?.Write("INFO", message, null);
    public static void Warn(string message, Exception? ex = null) => Current?.Write("WARN", message, ex);
    public static void Error(string message, Exception? ex = null) => Current?.Write("ERROR", message, ex);

    public void Write(string level, string message, Exception? ex)
    {
        try
        {
            var text = new StringBuilder()
                .Append(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture))
                .Append(' ').Append(level).Append(' ').AppendLine(message);
            if (ex is not null) text.AppendLine(ex.ToString());

            lock (_gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                var info = new FileInfo(_path);
                if (info.Exists && info.Length > MaxBytes) File.Move(_path, _path + ".1", overwrite: true);
                File.AppendAllText(_path, text.ToString(), Encoding.UTF8);
            }
        }
        catch (Exception)
        {
            // Logging is best-effort by design.
        }
    }
}
