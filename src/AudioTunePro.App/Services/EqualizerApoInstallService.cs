using System.IO;
using AudioTunePro.Core.Services;
using Microsoft.Win32;

namespace AudioTunePro.App.Services;

/// <summary>
/// Locates the Equalizer APO installation (the free, open-source system audio
/// engine AudioTune Pro drives) and manages the single "Include:" line it adds
/// to the user's config.txt — never touching the rest of that file.
/// </summary>
public sealed class EqualizerApoInstallService
{
    private const string IncludeFileName = ApoConfigWriter.IncludeFileName;
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

        ApoConfigWriter.Apply(configDir, renderedConfig);
    }
}
