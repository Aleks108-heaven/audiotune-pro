using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AudioTunePro.Core.Models;
using AudioTunePro.Core.Services;

namespace AudioTunePro.App.UiTests;

/// <summary>The line drawn behind the faders: where it sits, what it is made of, and that it never gets in the way.</summary>
[Collection(WpfCollection.Name)]
public sealed class ResponseCurveTests
{
    private const double Tolerance = 0.5; // pixels

    private readonly WpfFixture _wpf;

    public ResponseCurveTests(WpfFixture wpf) => _wpf = wpf;

    public static IEnumerable<object[]> BandsAndSizes() => MainWindowLayoutTests.BandsAndSizes();

    [Theory]
    [MemberData(nameof(BandsAndSizes))]
    public void The_drawn_line_runs_through_the_center_of_every_fader_cap(int bands, double width, double height)
    {
        _wpf.Run(() =>
        {
            var viewModel = new FakeMainViewModel(bands, fadersOnly: true);
            var window = MainWindowHarness.Open(viewModel, width, height);
            try
            {
                MainWindowHarness.Snapshot(window, $"b{bands}_curve_{width:0}x{height:0}", width, height);

                var view = MainWindowHarness.CurveOf(window);
                var line = MainWindowHarness.ReadCurveLine(view);
                var caps = MainWindowHarness.Caps(window);
                Assert.Equal(bands, caps.Count);
                Assert.Equal(viewModel.ResponseCurve.Count, line.Points.Count);

                // The line spans the whole row of faders, and the sample at band i's center is the point on the line that must meet cap i.
                Assert.InRange(line.Points[0].X, -Tolerance, Tolerance);
                Assert.InRange(line.Points[^1].X, view.ActualWidth - Tolerance, view.ActualWidth + Tolerance);
                for (int i = 0; i < bands; i++)
                {
                    var point = line.Points[FrequencyResponse.PointsPerBand * i + FrequencyResponse.PointsPerBand / 2];
                    var onLine = view.TranslatePoint(point, MainWindowHarness.Root(window));
                    var cap = MainWindowHarness.Center(caps[i], window);

                    Assert.True(Math.Abs(onLine.X - cap.X) <= Tolerance, $"band {i}: line at x {onLine.X:0.00}, cap at x {cap.X:0.00}");
                    Assert.True(Math.Abs(onLine.Y - cap.Y) <= Tolerance, $"band {i}: line at y {onLine.Y:0.00}, cap at y {cap.Y:0.00}");
                }
            }
            finally
            {
                MainWindowHarness.Dispose(window);
            }
        });
    }

    [Fact]
    public void The_curve_is_drawn_beneath_the_faders_and_takes_no_input()
    {
        _wpf.Run(() =>
        {
            var window = MainWindowHarness.Open(new FakeMainViewModel(15, fadersOnly: true));
            try
            {
                var curve = MainWindowHarness.CurveOf(window);
                var overlay = (Grid)VisualTreeHelper.GetParent(curve);
                var container = (Grid)VisualTreeHelper.GetParent(overlay);
                var faders = container.Children.OfType<ItemsControl>().Single();

                Assert.True(container.Children.IndexOf(overlay) < container.Children.IndexOf(faders),
                    "the curve must come before the faders in the same cell, so the caps are drawn over it");
                Assert.False(overlay.IsHitTestVisible);

                // A click on a cap lands on its slider, not on the curve behind it.
                var cap = MainWindowHarness.Center(MainWindowHarness.Caps(window)[7], window);
                var hit = VisualTreeHelper.HitTest(MainWindowHarness.Root(window), cap);
                Assert.NotNull(hit);
                Assert.NotNull(FindAncestor<Slider>(hit!.VisualHit));
            }
            finally
            {
                MainWindowHarness.Dispose(window);
            }
        });
    }

    [Fact]
    public void The_curve_is_neutral_white_in_use_and_a_flat_disabled_gray_when_bypassed()
    {
        _wpf.Run(() =>
        {
            var resources = Application.Current.Resources;

            var active = MainWindowHarness.Open(new FakeMainViewModel(15));
            var bypassed = MainWindowHarness.Open(new FakeMainViewModel(15, equalizerEnabled: false));
            try
            {
                Assert.Same(resources["text-primary"], MainWindowHarness.CurveOf(active).Stroke);

                var curve = MainWindowHarness.CurveOf(bypassed);
                Assert.Same(resources["text-disabled"], curve.Stroke);
                Assert.All(curve.Samples!, v => Assert.Equal(0, v));

                var line = MainWindowHarness.ReadCurveLine(curve);
                Assert.InRange(line.Bounds.Height, 0, line.Thickness + 0.01);        // one level stroke, no excursions
                Assert.InRange(line.Bounds.Top + line.Bounds.Height / 2, curve.ActualHeight / 2 - 0.01, curve.ActualHeight / 2 + 0.01);
            }
            finally
            {
                MainWindowHarness.Dispose(active);
                MainWindowHarness.Dispose(bypassed);
            }
        });
    }

    [Fact]
    public void The_line_is_drawn_again_when_the_view_model_publishes_a_new_curve()
    {
        // What every fader drag does: MainViewModel raises PropertyChanged(ResponseCurve) with a freshly computed array.
        _wpf.Run(() =>
        {
            var viewModel = new FakeMainViewModel(15, fadersOnly: true, gains: _ => 0);
            var window = MainWindowHarness.Open(viewModel);
            try
            {
                var view = MainWindowHarness.CurveOf(window);
                var flat = MainWindowHarness.ReadCurveLine(view);
                Assert.InRange(flat.Bounds.Height, 0, flat.Thickness + 0.01);

                var engine = EqEngine.Create(15);
                engine.Bands[7].GainDb = 10;
                engine.BassDb = 6;
                engine.Clarity.LowCutHz = 100;
                viewModel.ResponseCurve = FrequencyResponse.Sample(engine);
                MainWindowHarness.Layout(window, MainWindowHarness.DefaultWidth, MainWindowHarness.DefaultHeight);
                var changed = MainWindowHarness.ReadCurveLine(view);

                Assert.Equal(viewModel.ResponseCurve.Count, changed.Points.Count);
                Assert.True(changed.Bounds.Height > 50, $"the line should now rise and fall; its height is {changed.Bounds.Height:0.0} px");
            }
            finally
            {
                MainWindowHarness.Dispose(window);
            }
        });
    }

    [Fact]
    public void The_curve_uses_the_faders_scale_and_holds_what_goes_beyond_it_at_the_panels_edge()
    {
        _wpf.Run(() =>
        {
            var viewModel = new FakeMainViewModel(15, fadersOnly: true);
            var window = MainWindowHarness.Open(viewModel);
            try
            {
                var view = MainWindowHarness.CurveOf(window);
                int points = FrequencyResponse.Sample(EqEngine.Create(15)).Length;
                double height = view.ActualHeight;
                Assert.Equal(12, view.RangeDb); // the faders' +12 / -12 dB

                MainWindowHarness.CurveLine Draw(double db)
                {
                    viewModel.ResponseCurve = Enumerable.Repeat(db, points).ToArray();
                    MainWindowHarness.Layout(window, MainWindowHarness.DefaultWidth, MainWindowHarness.DefaultHeight);
                    return MainWindowHarness.ReadCurveLine(view);
                }

                // +30 dB everywhere: a level line whose outer edge is the panel's top edge, fully inside it.
                var up = Draw(30);
                Assert.InRange(up.Bounds.Top, -0.01, 0.01);
                Assert.InRange(up.Bounds.Height, 0, up.Thickness + 0.01);

                // -30 dB: the same along the bottom edge.
                var down = Draw(-30);
                Assert.InRange(down.Bounds.Bottom, height - 0.01, height + 0.01);
                Assert.InRange(down.Bounds.Height, 0, down.Thickness + 0.01);

                // 0 dB, and a value that is not a number (drawn as 0 dB), sit on the middle line; +12 / -12 sit on the edges too.
                foreach (var flat in new[] { 0.0, double.NaN })
                {
                    var middle = Draw(flat);
                    Assert.InRange(middle.Bounds.Top + middle.Bounds.Height / 2, height / 2 - 0.01, height / 2 + 0.01);
                }

                Assert.InRange(Draw(12).Bounds.Top, -0.01, 0.01);
                Assert.InRange(Draw(-12).Bounds.Bottom, height - 0.01, height + 0.01);
            }
            finally
            {
                MainWindowHarness.Dispose(window);
            }
        });
    }

    private static T? FindAncestor<T>(DependencyObject? start) where T : DependencyObject
    {
        var node = start;
        while (node is not null)
        {
            if (node is T match) return match;
            node = node is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(node)
                : LogicalTreeHelper.GetParent(node);
        }

        return null;
    }
}
