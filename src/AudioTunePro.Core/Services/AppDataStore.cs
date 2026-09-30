using System.Text.Json;
using AudioTunePro.Core.Models;

namespace AudioTunePro.Core.Services;

/// <summary>
/// Reads/writes AudioTune Pro's own app data: user settings and user-created
/// presets, stored as JSON under %AppData%\AudioTunePro. Independent of the
/// Equalizer APO config files, which are a rendered *output* of this data.
/// </summary>
public sealed class AppDataStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    /// <summary>Settings and presets are a few KB; anything far larger is corrupt or hostile and is not read.</summary>
    private const long MaxFileBytes = 2 * 1024 * 1024;

    public string RootDirectory { get; }
    private string SettingsPath => Path.Combine(RootDirectory, "settings.json");
    private string PresetsPath => Path.Combine(RootDirectory, "user-presets.json");

    public AppDataStore(string? rootDirectory = null)
    {
        RootDirectory = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AudioTunePro");
        try { Directory.CreateDirectory(RootDirectory); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Read-only profile: loads fall back to defaults and saves report failure instead of crashing.
        }
    }

    public AppSettings LoadSettings()
    {
        if (!File.Exists(SettingsPath) || IsTooLarge(SettingsPath)) return new AppSettings();
        try
        {
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch (Exception ex) when (IsStoreError(ex))
        {
            return new AppSettings();
        }
    }

    /// <summary>Saves settings; returns false (instead of throwing) if the disk is unavailable or read-only.</summary>
    public bool SaveSettings(AppSettings settings) =>
        WriteAtomic(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));

    public List<Preset> LoadUserPresets()
    {
        if (!File.Exists(PresetsPath) || IsTooLarge(PresetsPath)) return new List<Preset>();
        try
        {
            var json = File.ReadAllText(PresetsPath);
            return JsonSerializer.Deserialize<List<Preset>>(json) ?? new List<Preset>();
        }
        catch (Exception ex) when (IsStoreError(ex))
        {
            return new List<Preset>();
        }
    }

    /// <summary>Saves user presets; returns false (instead of throwing) if the disk is unavailable or read-only.</summary>
    public bool SaveUserPresets(IEnumerable<Preset> presets)
    {
        var list = presets.Where(p => !p.IsBuiltIn).ToList();
        return WriteAtomic(PresetsPath, JsonSerializer.Serialize(list, JsonOptions));
    }

    private static bool IsTooLarge(string path)
    {
        try { return new FileInfo(path).Length > MaxFileBytes; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return true; }
    }

    private static bool IsStoreError(Exception ex) =>
        ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException;

    /// <summary>Write-then-replace, so a crash or power loss mid-write can't leave a truncated JSON file.</summary>
    private bool WriteAtomic(string path, string contents)
    {
        try
        {
            Directory.CreateDirectory(RootDirectory);
            var temp = path + ".tmp";
            File.WriteAllText(temp, contents);
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
