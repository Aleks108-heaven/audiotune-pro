using System.Globalization;
using System.Text.RegularExpressions;

namespace AudioTunePro.Core.Services;

/// <summary>What the rest of the user's Equalizer APO setup adds on top of AudioTune Pro's own include file.</summary>
public sealed record ExternalConfigInfo(double PreampDb, bool LoadsPlugins)
{
    public static readonly ExternalConfigInfo None = new(0, false);
}

/// <summary>
/// Reads the user's own Equalizer APO configuration (config.txt and the files it includes from the same
/// folder, never AudioTune Pro's include file) for two things AudioTune Pro cannot otherwise see:
/// extra <c>Preamp:</c> gain, which stacks on top of ours and defeated the clipping limiter, and
/// <c>VSTPlugin:</c> lines, which load third-party code into the Windows audio service.
/// This is an estimate: it adds up every Preamp line and ignores Device:/Channel: conditions.
/// </summary>
public static class ExternalApoConfig
{
    private const long MaxFileBytes = 1024 * 1024;
    private const int MaxFiles = 16;
    private const int MaxIncludeDepth = 3;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static readonly Regex PreampLine = new(@"^\s*Preamp\s*:\s*(?<v>[+-]?\d+(?:[.,]\d+)?)", Options, RegexTimeout);
    private static readonly Regex PluginLine = new(@"^\s*VSTPlugin\s*:", Options, RegexTimeout);
    private static readonly Regex IncludeLine = new(@"^\s*Include\s*:\s*(?<f>.+?)\s*$", Options, RegexTimeout);

    public static ExternalConfigInfo Scan(string mainConfigPath)
    {
        double preamp = 0;
        bool plugins = false;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string directory;
        try { directory = Path.GetDirectoryName(Path.GetFullPath(mainConfigPath)) ?? string.Empty; }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return ExternalConfigInfo.None;
        }

        void Visit(string path, int depth)
        {
            if (visited.Count >= MaxFiles || !visited.Add(path)) return;
            if (string.Equals(Path.GetFileName(path), ApoConfigWriter.IncludeFileName, StringComparison.OrdinalIgnoreCase)) return;

            string[] lines;
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length > MaxFileBytes) return;
                lines = File.ReadAllLines(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return;
            }

            foreach (var raw in lines)
            {
                var line = raw.TrimStart();
                if (line.Length == 0 || line[0] == '#') continue;

                try
                {
                    var m = PreampLine.Match(line);
                    if (m.Success &&
                        double.TryParse(m.Groups["v"].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var db) &&
                        double.IsFinite(db))
                    {
                        preamp += db;
                        continue;
                    }

                    if (PluginLine.IsMatch(line)) { plugins = true; continue; }

                    var inc = IncludeLine.Match(line);
                    if (inc.Success && depth < MaxIncludeDepth)
                    {
                        // Only plain file names next to config.txt are followed: never a path the app would then read blindly.
                        var name = inc.Groups["f"].Value.Trim('"');
                        if (name.Length > 0 && name == Path.GetFileName(name) && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0)
                            Visit(Path.Combine(directory, name), depth + 1);
                    }
                }
                catch (RegexMatchTimeoutException)
                {
                    // A pathological line is skipped rather than allowed to stall the scan.
                }
            }
        }

        Visit(Path.GetFullPath(mainConfigPath), 0);
        return preamp == 0 && !plugins ? ExternalConfigInfo.None : new ExternalConfigInfo(preamp, plugins);
    }
}
