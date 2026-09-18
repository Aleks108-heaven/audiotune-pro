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

    public string RootDirectory { get; }
    private string SettingsPath => Path.Combine(RootDirectory, "settings.json");
    private string PresetsPath => Path.Combine(RootDirectory, "user-presets.json");

    public AppDataStore(string? rootDirectory = null)
    {
        RootDirectory = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AudioTunePro");
        Directory.CreateDirectory(RootDirectory);
    }

    public AppSettings LoadSettings()
    {
        if (!File.Exists(SettingsPath)) return new AppSettings();
        try
        {
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return new AppSettings();
        }
    }

    public void SaveSettings(AppSettings settings)
    {
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }

    public List<Preset> LoadUserPresets()
    {
        if (!File.Exists(PresetsPath)) return new List<Preset>();
        try
        {
            var json = File.ReadAllText(PresetsPath);
            return JsonSerializer.Deserialize<List<Preset>>(json) ?? new List<Preset>();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return new List<Preset>();
        }
    }

    public void SaveUserPresets(IEnumerable<Preset> presets)
    {
        var list = presets.Where(p => !p.IsBuiltIn).ToList();
        File.WriteAllText(PresetsPath, JsonSerializer.Serialize(list, JsonOptions));
    }
}
