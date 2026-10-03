namespace AudioTunePro.Core.Services;

/// <summary>
/// The file side of driving Equalizer APO: writes AudioTune Pro's own include file and makes sure the
/// user's config.txt references it exactly once, never touching the rest of that file. Kept free of
/// registry/UI code so it can be tested against a temp directory.
/// </summary>
public static class ApoConfigWriter
{
    public const string IncludeFileName = "AudioTunePro.txt";
    private const string IncludeLine = "Include: " + IncludeFileName;

    /// <summary>Writes the snippet (write-then-replace, so APO's hot-reload never sees a half file) and ensures the include line.</summary>
    public static void Apply(string configDirectory, string renderedConfig)
    {
        Directory.CreateDirectory(configDirectory);
        var includePath = Path.Combine(configDirectory, IncludeFileName);
        var tempPath = includePath + ".tmp";
        File.WriteAllText(tempPath, renderedConfig);
        File.Move(tempPath, includePath, overwrite: true);

        EnsureIncludeLine(Path.Combine(configDirectory, "config.txt"));
    }

    public static void EnsureIncludeLine(string mainConfigPath)
    {
        if (!File.Exists(mainConfigPath))
        {
            File.WriteAllText(mainConfigPath, IncludeLine + Environment.NewLine);
            return;
        }

        var text = File.ReadAllText(mainConfigPath);
        var alreadyIncluded = text.Split('\n').Any(l =>
            l.Trim().Equals(IncludeLine, StringComparison.OrdinalIgnoreCase));
        if (alreadyIncluded) return;

        // Start on a fresh line even if the file doesn't end with one.
        var lead = text.Length == 0 || text.EndsWith('\n') ? string.Empty : Environment.NewLine;
        File.AppendAllText(mainConfigPath,
            lead + "# Added by AudioTune Pro:" + Environment.NewLine + IncludeLine + Environment.NewLine);
    }
}
