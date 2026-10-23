using System.Text.RegularExpressions;
using AudioTunePro.App.ViewModels;

namespace AudioTunePro.App.UiTests;

public class MainWindowBindingTests
{
    // Arguments a binding can start with instead of a path.
    private static readonly HashSet<string> NotAPath = new()
    {
        "RelativeSource", "ElementName", "Source", "Converter", "ConverterParameter", "ConverterCulture",
        "Mode", "StringFormat", "FallbackValue", "TargetNullValue", "UpdateSourceTrigger",
    };

    [Fact]
    public void Every_binding_in_the_window_names_a_property_the_real_view_models_have()
    {
        var main = typeof(MainViewModel).GetProperties().Select(p => p.Name).ToHashSet();
        var band = typeof(BandViewModel).GetProperties().Select(p => p.Name).ToHashSet();

        var paths = Regex.Matches(MainWindowHarness.SourceXaml, @"\{Binding\s+(?:Path=)?([A-Za-z_]\w*)")
            .Select(m => m.Groups[1].Value)
            .Where(p => !NotAPath.Contains(p))
            .Distinct()
            .OrderBy(p => p)
            .ToList();

        Assert.True(paths.Count > 30, "the pattern should find the window's bindings; it found " + paths.Count);
        // Bindings inside the band template resolve against BandViewModel, all others against MainViewModel.
        var missing = paths.Where(p => !main.Contains(p) && !band.Contains(p)).ToList();
        Assert.True(missing.Count == 0, "the window binds to properties that neither real view model has: " + string.Join(", ", missing));
    }

    [Fact]
    public void The_window_binds_to_the_band_count_the_clarity_controls_and_the_response_curve()
    {
        var main = typeof(MainViewModel).GetProperties().Select(p => p.Name).ToHashSet();

        foreach (var name in new[] { "Bands10", "Bands15", "Bands31", "GraphicEqTitle", "ReadoutRowHeight", "ResponseCurve",
                                     "ClarityAmount", "LowCutHz", "LowCutText" })
            Assert.Contains(name, main);
    }
}
