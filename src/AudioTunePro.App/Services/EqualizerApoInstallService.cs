using System.IO;
using Microsoft.Win32;

namespace AudioTunePro.App.Services;

/// <summary>
/// Locates the Equalizer APO installation (the free, open-source system audio
/// engine AudioTune Pro drives) and manages the single "Include:" line it adds
/// to the user's config.txt — never touching the rest of that file.
/// </summary>
public sealed class EqualizerApoInstallService
{
    private const string IncludeFileName = "AudioTunePro.txt";
    public const string DownloadUrl = "https://sourceforge.net/projects/equalizerapo/";

    public string? FindInstallDirectory()
    {
        // Equalizer APO writes its install location to the registry (both 32/64-bit views).
        foreach (var hive in new[] { RegistryHive.LocalMachine })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var key = baseKey.OpenSubKey(
                        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\EqualizerAPO");
                    var installLocation = key?.GetValue("InstallLocation") as string;
                    if (!string.IsNullOrWhiteSpace(installLocation) && Directory.Exists(installLocation))
                        return installLocation;
                }
                catch (System.Security.SecurityException) { }
                catch (IOException) { }
            }
        }

        // Fall back to the conventional default path.
        var candidates = new[]
        {
            @"C:\Program Files\EqualizerAPO",
            @"C:\Program Files (x86)\EqualizerAPO",
        };
        return candidates.FirstOrDefault(Directory.Exists);
    }

    public bool IsInstalled => FindInstallDirectory() is not null;

    public string? GetConfigDirectory()
    {
        var installDir = FindInstallDirectory();
        return installDir is null ? null : Path.Combine(installDir, "config");
    }

    public string? GetIncludeFilePath()
    {
        var configDir = GetConfigDirectory();
        return configDir is null ? null : Path.Combine(configDir, IncludeFileName);
    }

    /// <summary>
    /// Writes the rendered EQ snippet to AudioTune Pro's own include file and makes
    /// sure config.txt references it exactly once. Equalizer APO watches config.txt
    /// (and anything it includes) for changes and hot-reloads automatically.
    /// </summary>
    public void ApplyConfig(string renderedConfig)
    {
        var configDir = GetConfigDirectory()
            ?? throw new InvalidOperationException("Equalizer APO is not installed.");

        Directory.CreateDirectory(configDir);
        var includePath = Path.Combine(configDir, IncludeFileName);
        // Write-then-replace so Equalizer APO's hot-reload never reads a half-written file.
        var tempPath = includePath + ".tmp";
        File.WriteAllText(tempPath, renderedConfig);
        File.Move(tempPath, includePath, overwrite: true);

        var mainConfigPath = Path.Combine(configDir, "config.txt");
        EnsureIncludeLine(mainConfigPath);
    }

    private void EnsureIncludeLine(string mainConfigPath)
    {
        const string includeLine = "Include: " + IncludeFileName;

        if (!File.Exists(mainConfigPath))
        {
            File.WriteAllText(mainConfigPath, includeLine + Environment.NewLine);
            return;
        }

        var lines = File.ReadAllLines(mainConfigPath);
        bool alreadyIncluded = lines.Any(l =>
            l.Trim().Equals(includeLine, StringComparison.OrdinalIgnoreCase));

        if (!alreadyIncluded)
        {
            File.AppendAllText(mainConfigPath,
                Environment.NewLine + "# Added by AudioTune Pro:" +
                Environment.NewLine + includeLine + Environment.NewLine);
        }
    }
}
