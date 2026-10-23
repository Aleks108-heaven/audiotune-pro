using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AudioTunePro.App.UiTests;

/// <summary>
/// Layout rules of the main window, checked on the real XAML at the default and at the minimum window size, for each
/// graphic-EQ resolution. Geometry only (positions, overlaps, fonts, transforms): no pixel comparisons, so the results do
/// not depend on the machine's fonts or display scaling beyond a small tolerance. Each test also saves a picture under
/// <see cref="MainWindowHarness.SnapshotDirectory"/>.
/// </summary>
[Collection(WpfCollection.Name)]
public sealed class MainWindowLayoutTests
{
    private const double Tolerance = 0.5; // pixels

    private readonly WpfFixture _wpf;

    public MainWindowLayoutTests(WpfFixture wpf) => _wpf = wpf;

    public static IEnumerable<object[]> BandsAndSizes()
    {
        foreach (var bands in new[] { 10, 15, 31 })
        {
            yield return new object[] { bands, MainWindowHarness.DefaultWidth, MainWindowHarness.DefaultHeight };
            yield return new object[] { bands, MainWindowHarness.MinimumWidth, MainWindowHarness.MinimumHeight };
        }
    }

    public static IEnumerable<object[]> Bands() => new[] { 10, 15, 31 }.Select(b => new object[] { b });

    /// <summary>The widest readouts there are, "+12.0" and "-12.0", on alternating bands, plus a plain "0.0".</summary>
    private static double WidestReadouts(int band) => (band % 3) switch { 0 => 12, 1 => -12, _ => 0 };

    [Theory]
    [MemberData(nameof(BandsAndSizes))]
    public void The_window_renders_without_a_single_binding_error(int bands, double width, double height)
    {
        var errors = _wpf.Run(() => MainWindowHarness.CaptureBindingErrors(() =>
        {
            var window = MainWindowHarness.Open(new FakeMainViewModel(bands), width, height);
            MainWindowHarness.Snapshot(window, $"b{bands}_{width:0}x{height:0}", width, height);
            MainWindowHarness.Dispose(window);
        }));

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(31)]
    public void The_bypassed_window_renders_without_binding_errors_too(int bands)
    {
        var errors = _wpf.Run(() => MainWindowHarness.CaptureBindingErrors(() =>
        {
            var window = MainWindowHarness.Open(new FakeMainViewModel(bands, equalizerEnabled: false));
            MainWindowHarness.Snapshot(window, $"b{bands}_bypassed", MainWindowHarness.DefaultWidth, MainWindowHarness.DefaultHeight);
            MainWindowHarness.Dispose(window);
        }));

        Assert.Empty(errors);
    }

    [Theory]
    [MemberData(nameof(BandsAndSizes))]
    public void Readouts_never_overlap_each_other_or_the_faders(int bands, double width, double height)
    {
        _wpf.Run(() =>
        {
            var window = MainWindowHarness.Open(new FakeMainViewModel(bands, gains: WidestReadouts), width, height);
            try
            {
                var values = MainWindowHarness.TextsWithStyle(window, "fader-value");
                var labels = MainWindowHarness.TextsWithStyle(window, "band-label");
                var faders = MainWindowHarness.Faders(window);
                Assert.Equal(bands, values.Count);
                Assert.Equal(bands, labels.Count);
                Assert.Equal(bands, faders.Count);

                for (int i = 0; i < bands; i++)
                {
                    var value = MainWindowHarness.Bounds(values[i], window);
                    var label = MainWindowHarness.Bounds(labels[i], window);
                    var fader = MainWindowHarness.Bounds(faders[i], window);

                    Assert.True(value.Bottom <= fader.Top + Tolerance, $"band {i}: the dB value reaches down into the fader ({value.Bottom:0.0} > {fader.Top:0.0})");
                    Assert.True(label.Top >= fader.Bottom - Tolerance, $"band {i}: the frequency label reaches up into the fader ({label.Top:0.0} < {fader.Bottom:0.0})");

                    if (i == bands - 1) continue;
                    AssertSeparate(value, MainWindowHarness.Bounds(values[i + 1], window), $"dB values of bands {i} and {i + 1}");
                    AssertSeparate(label, MainWindowHarness.Bounds(labels[i + 1], window), $"frequency labels of bands {i} and {i + 1}");
                }
            }
            finally
            {
                MainWindowHarness.Dispose(window);
            }
        });
    }

    [Theory]
    [MemberData(nameof(Bands))]
    public void The_faders_line_up_whatever_the_readouts_say(int bands)
    {
        _wpf.Run(() =>
        {
            var window = MainWindowHarness.Open(new FakeMainViewModel(bands, gains: WidestReadouts));
            try
            {
                // Rotated readouts differ in length ("0.0" vs "+12.0"); the rows around the faders are fixed height, so no
                // fader may start higher, end lower or be taller than another.
                var bounds = MainWindowHarness.Faders(window).Select(f => MainWindowHarness.Bounds(f, window)).ToList();

                Assert.Equal(bands, bounds.Count);
                Assert.InRange(bounds.Max(b => b.Top) - bounds.Min(b => b.Top), 0, Tolerance);
                Assert.InRange(bounds.Max(b => b.Height) - bounds.Min(b => b.Height), 0, Tolerance);
            }
            finally
            {
                MainWindowHarness.Dispose(window);
            }
        });
    }

    [Fact]
    public void Readouts_shrink_at_15_bands_and_stand_on_end_at_31()
    {
        _wpf.Run(() =>
        {
            foreach (var (bands, fontSize, rotated) in new[] { (10, 13.0, false), (15, 11.0, false), (31, 11.0, true) })
            {
                var window = MainWindowHarness.Open(new FakeMainViewModel(bands));
                try
                {
                    var texts = MainWindowHarness.TextsWithStyle(window, "fader-value")
                        .Concat(MainWindowHarness.TextsWithStyle(window, "band-label")).ToList();
                    Assert.Equal(bands * 2, texts.Count);

                    foreach (var text in MainWindowHarness.TextsWithStyle(window, "fader-value"))
                        Assert.Equal(fontSize, text.FontSize);
                    Assert.All(texts, t => Assert.Equal(rotated, t.LayoutTransform is RotateTransform { Angle: -90 }));
                }
                finally
                {
                    MainWindowHarness.Dispose(window);
                }
            }
        });
    }

    [Fact]
    public void The_limiters_peak_badge_is_not_pushed_out_of_sight_at_the_default_size()
    {
        // Putting the Clarity controls in the sidebar once pushed the whole Limiter section below the fold at this size.
        // Today the sidebar is already a few pixels taller than the window at 100% scaling (the badge's bottom edge, about
        // 3 px, is cut off and the "Auto-gain trim" line below it needs a scroll), so the rule is that the badge is mostly
        // in view: its midpoint is visible. Any new control above it, a row or more, breaks that.
        _wpf.Run(() =>
        {
            var window = MainWindowHarness.Open(new FakeMainViewModel(15));
            try
            {
                var badge = MainWindowHarness.Descendants<Border>(MainWindowHarness.Root(window))
                    .Single(b => ReferenceEquals(b.Style, Application.Current.Resources["PeakIndicatorBadge"]));
                var sidebar = MainWindowHarness.Descendants<ScrollViewer>(MainWindowHarness.Root(window)).First();

                var area = badge.TransformToVisual(sidebar).TransformBounds(new Rect(0, 0, badge.ActualWidth, badge.ActualHeight));
                double midpoint = area.Top + area.Height / 2;

                Assert.True(midpoint <= sidebar.ViewportHeight,
                    $"the peak badge's midpoint is at {midpoint:0.0} px but the sidebar shows only {sidebar.ViewportHeight:0.0} px without scrolling");
            }
            finally
            {
                MainWindowHarness.Dispose(window);
            }
        });
    }

    [Fact]
    public void The_window_keeps_the_minimum_size_the_design_docs_state()
    {
        _wpf.Run(() =>
        {
            var window = MainWindowHarness.Open(new FakeMainViewModel(15));
            try
            {
                Assert.Equal(MainWindowHarness.MinimumWidth, window.MinWidth);
                Assert.Equal(MainWindowHarness.MinimumHeight, window.MinHeight);
            }
            finally
            {
                MainWindowHarness.Dispose(window);
            }
        });
    }

    private static void AssertSeparate(Rect a, Rect b, string what)
    {
        var overlap = Rect.Intersect(a, b);
        bool separate = overlap.IsEmpty || overlap.Width < Tolerance || overlap.Height < Tolerance;
        Assert.True(separate, $"the {what} overlap by {overlap.Width:0.0} x {overlap.Height:0.0} px ({a} against {b})");
    }
}
