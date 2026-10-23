namespace AudioTunePro.Core.Models;

/// <summary>Persisted app-level settings, plus the live (possibly unsaved) EQ state.</summary>
public sealed class AppSettings
{
    public string ActivePresetName { get; set; } = "ASUS Vivobook Speakers";
    public bool StartWithWindows { get; set; }
    public bool StartMinimizedToTray { get; set; } = true;
    public bool ShowLevelMeter { get; set; } = true;

    /// <summary>
    /// Graphic-EQ resolution (10, 15 or 31 bands, see <see cref="BandLayouts"/>). 0 until the user has the choice,
    /// which includes everyone upgrading from the fixed 10-band build: they start on the default resolution.
    /// </summary>
    public int BandCount { get; set; }

    /// <summary>
    /// The EQ state exactly as last edited, so restarting the app shows (and re-applies) what was
    /// actually playing rather than snapping back to the preset's saved values. Null on first run.
    /// </summary>
    public EqEngine? LiveEngine { get; set; }

    /// <summary>Set once the "still running in the tray" hint has been shown.</summary>
    public bool TrayHintShown { get; set; }
}
