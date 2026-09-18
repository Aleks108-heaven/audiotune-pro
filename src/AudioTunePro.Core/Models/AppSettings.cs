namespace AudioTunePro.Core.Models;

/// <summary>Persisted app-level (not EQ-curve) settings.</summary>
public sealed class AppSettings
{
    public string ActivePresetName { get; set; } = "ASUS Vivobook Speakers";
    public bool StartWithWindows { get; set; }
    public bool StartMinimizedToTray { get; set; } = true;
    public bool ShowLevelMeter { get; set; } = true;
}
