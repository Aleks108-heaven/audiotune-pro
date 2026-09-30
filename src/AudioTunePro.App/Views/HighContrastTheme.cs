using System.Windows;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using SystemColors = System.Windows.SystemColors;

namespace AudioTunePro.App.Views;

/// <summary>
/// When a Windows high-contrast theme is active, every design-token brush is remapped to the
/// matching system color so the custom-drawn UI stays readable (the dark palette would otherwise
/// ignore the user's contrast settings). Brushes are mutated in place because the styles resolve
/// them with StaticResource at load time. A theme change takes effect the next time the app starts.
/// </summary>
internal static class HighContrastTheme
{
    public static void ApplyIfActive(ResourceDictionary resources)
    {
        if (!SystemParameters.HighContrast) return;

        var window = SystemColors.WindowColor;
        var text = SystemColors.WindowTextColor;
        var gray = SystemColors.GrayTextColor;
        var highlight = SystemColors.HighlightColor;
        var highlightText = SystemColors.HighlightTextColor;
        var hot = SystemColors.HotTrackColor;

        var map = new Dictionary<string, Color>
        {
            ["app-bg-top"] = window, ["app-bg-bottom"] = window,
            ["surface-panel"] = window, ["surface-raised"] = window, ["warn-bg"] = window,
            ["toggle-track-off"] = window, ["track-fill"] = window,
            ["border-panel"] = text, ["divider"] = text, ["warn-border"] = text, ["track-guide"] = text,
            ["text-primary"] = text, ["text-secondary"] = text,
            ["text-disabled"] = gray, ["accent-muted"] = gray,
            ["accent-teal"] = highlight, ["toggle-track-on"] = highlight, ["focus-ring"] = highlight,
            ["signal-safe"] = highlight, ["ink-on-accent"] = highlightText,
            ["signal-warn"] = hot, ["signal-danger"] = hot,
        };

        foreach (var (key, color) in map)
        {
            if (resources[key] is SolidColorBrush { IsFrozen: false } brush) brush.Color = color;
        }

        if (resources["app-bg-gradient"] is LinearGradientBrush { IsFrozen: false } bg)
            foreach (var stop in bg.GradientStops) stop.Color = window;

        if (resources["level-meter-gradient"] is LinearGradientBrush { IsFrozen: false } meter)
            foreach (var stop in meter.GradientStops) stop.Color = highlight;
    }
}
