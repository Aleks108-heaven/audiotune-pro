using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using AudioTunePro.Core.Models;
using AudioTunePro.Core.Presets;
using AudioTunePro.Core.Services;
using Xunit;

namespace AudioTunePro.Core.Tests;

public class FilterResponseTests
{
    private static FilterSpec Peak(double fc, double gain, double q) => new("PK", FilterShape.Peaking, fc, gain, q);
    private static FilterSpec LowShelf(double fc, double gain, double q = 0.9) => new("LSC", FilterShape.LowShelf, fc, gain, q);
    private static FilterSpec HighShelf(double fc, double gain, double q = 0.9) => new("HSC", FilterShape.HighShelf, fc, gain, q);
    private static FilterSpec HighPass(double fc, double? q = null) => new("HP", FilterShape.HighPass, fc, null, q);

    [Theory]
    [InlineData(300, -2.5, 0.9)]
    [InlineData(3200, 2.0, 0.8)]
    [InlineData(1000, 12, 1.0)]
    [InlineData(80, -9, 4.0)]
    public void A_peaking_filter_reaches_exactly_its_gain_at_its_center(double fc, double gain, double q) =>
        Assert.Equal(gain, FilterResponse.GainDb(Peak(fc, gain, q), fc), 9);

    [Fact]
    public void A_peaking_filter_leaves_distant_frequencies_alone()
    {
        var f = Peak(1000, 9, 2.0);

        Assert.InRange(FilterResponse.GainDb(f, 20), -0.05, 0.05);
        Assert.InRange(FilterResponse.GainDb(f, 20000), -0.3, 0.3);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(1000)]
    [InlineData(15000)]
    public void A_zero_gain_filter_changes_nothing(double hz)
    {
        Assert.Equal(0, FilterResponse.GainDb(Peak(1000, 0, 1), hz), 9);
        Assert.Equal(0, FilterResponse.GainDb(LowShelf(150, 0), hz), 9);
        Assert.Equal(0, FilterResponse.GainDb(HighShelf(6500, 0), hz), 9);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(-9)]
    [InlineData(12)]
    public void A_low_shelf_applies_its_full_gain_at_DC_and_none_at_the_top(double gain)
    {
        var f = LowShelf(150, gain);

        Assert.Equal(gain, FilterResponse.GainDb(f, 0.001), 3);
        Assert.Equal(0, FilterResponse.GainDb(f, FilterResponse.ReferenceSampleRate / 2), 9);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(-9)]
    [InlineData(12)]
    public void A_high_shelf_applies_its_full_gain_at_the_top_and_none_at_DC(double gain)
    {
        var f = HighShelf(6500, gain);

        Assert.Equal(gain, FilterResponse.GainDb(f, FilterResponse.ReferenceSampleRate / 2), 9);
        Assert.Equal(0, FilterResponse.GainDb(f, 0.001), 3);
    }

    [Fact]
    public void A_boost_shelf_is_a_boost_everywhere_on_its_own_side()
    {
        var low = LowShelf(150, 6);
        var high = HighShelf(6500, 6);

        Assert.InRange(FilterResponse.GainDb(low, 30), 5.0, 6.8);
        Assert.InRange(FilterResponse.GainDb(low, 1000), -0.2, 0.5);
        Assert.InRange(FilterResponse.GainDb(high, 16000), 5.0, 6.8);
        Assert.InRange(FilterResponse.GainDb(high, 500), -0.2, 0.5);
    }

    [Fact]
    public void A_high_pass_defaults_to_a_butterworth_Q_and_is_3_dB_down_at_its_corner()
    {
        var f = HighPass(80);

        Assert.Equal(20 * Math.Log10(Math.Sqrt(0.5)), FilterResponse.GainDb(f, 80), 6);   // -3.0103 dB
        Assert.InRange(FilterResponse.GainDb(f, 40), -12.8, -11.8);                       // 12 dB per octave below the corner
        Assert.InRange(FilterResponse.GainDb(f, 8), -40.3, -39.7);                        // 40 dB per decade
        Assert.InRange(FilterResponse.GainDb(f, 800), -0.02, 0.0);                        // untouched well above it
    }

    [Fact]
    public void A_high_pass_removes_everything_at_DC_without_producing_infinity()
    {
        double db = FilterResponse.GainDb(HighPass(80), 1e-9);

        Assert.True(double.IsFinite(db));
        Assert.True(db <= -100);
        Assert.True(db >= FilterResponse.FloorDb);
    }

    [Fact]
    public void A_filter_that_cannot_be_designed_has_no_effect()
    {
        Assert.Equal(0, FilterResponse.GainDb(Peak(30000, 6, 1), 1000));          // corner above half the sample rate
        Assert.Equal(0, FilterResponse.GainDb(Peak(1000, double.NaN, 1), 1000));
        Assert.Equal(0, FilterResponse.GainDb(Peak(1000, 6, 0), 1000));           // Q of zero
        Assert.Equal(0, FilterResponse.GainDb(HighPass(double.PositiveInfinity), 1000));
        Assert.Equal(0, FilterResponse.GainDb(Peak(1000, 6, 1), double.NaN));
    }
}

public class FrequencyResponseTests
{
    private static EqEngine WithRandomGains(int bands, int seed)
    {
        var rng = new Random(seed);
        var engine = EqEngine.Create(bands);
        foreach (var b in engine.Bands) b.GainDb = Math.Round(rng.NextDouble() * 24 - 12, 1);
        return engine;
    }

    [Theory]
    [InlineData(10)]
    [InlineData(15)]
    [InlineData(31)]
    public void There_are_eight_samples_per_band_column_plus_one(int bands) =>
        Assert.Equal(bands * 8 + 1, FrequencyResponse.Sample(EqEngine.Create(bands)).Length);

    [Theory]
    [InlineData(10)]
    [InlineData(15)]
    [InlineData(31)]
    public void A_flat_engine_draws_a_flat_line(int bands) =>
        Assert.All(FrequencyResponse.Sample(EqEngine.Create(bands)), v => Assert.Equal(0, v, 9));

    [Theory]
    [InlineData(10, 1)]
    [InlineData(15, 2)]
    [InlineData(31, 3)]
    public void The_curve_passes_exactly_through_every_fader_value_at_its_band_center(int bands, int seed)
    {
        var engine = WithRandomGains(bands, seed);

        var curve = FrequencyResponse.Sample(engine);

        for (int i = 0; i < bands; i++)
            Assert.Equal(engine.Bands[i].GainDb, curve[8 * i + 4], 9);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(31)]
    public void Halfway_between_two_bands_the_curve_is_the_average_of_their_gains(int bands)
    {
        var engine = WithRandomGains(bands, 7);

        var curve = FrequencyResponse.Sample(engine);

        for (int i = 0; i < bands - 1; i++)
            Assert.Equal((engine.Bands[i].GainDb + engine.Bands[i + 1].GainDb) / 2, curve[8 * i + 8], 9);
    }

    [Fact]
    public void Beyond_the_outer_bands_the_gain_of_the_end_band_is_held()
    {
        var engine = new EqEngine();
        engine.Bands[0].GainDb = 5;
        engine.Bands[^1].GainDb = -7;

        var curve = FrequencyResponse.Sample(engine);

        Assert.All(curve.Take(5), v => Assert.Equal(5, v, 9));                 // from the left edge to the first center
        Assert.All(curve.Skip(8 * 9 + 4), v => Assert.Equal(-7, v, 9));        // from the last center to the right edge
    }

    [Theory]
    [InlineData(10)]
    [InlineData(15)]
    [InlineData(31)]
    public void Band_positions_map_back_to_the_band_frequencies(int bands)
    {
        var centers = BandLayouts.Frequencies(bands);

        for (int i = 0; i < bands; i++)
            Assert.Equal(centers[i], FrequencyResponse.FrequencyAt(centers, i), 9);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(15)]
    [InlineData(31)]
    public void Frequency_rises_steadily_with_position_and_the_edges_sit_half_a_band_out(int bands)
    {
        var centers = BandLayouts.Frequencies(bands);
        double previous = 0;
        for (double p = -0.5; p <= bands - 0.5 + 1e-9; p += 0.125)
        {
            double f = FrequencyResponse.FrequencyAt(centers, p);
            Assert.True(f > previous);
            previous = f;
        }

        Assert.Equal(centers[0] / Math.Sqrt(centers[1] / centers[0]), FrequencyResponse.FrequencyAt(centers, -0.5), 9);
        Assert.Equal(centers[^1] * Math.Sqrt(centers[^1] / centers[^2]), FrequencyResponse.FrequencyAt(centers, bands - 0.5), 9);
        // The ten-band layout's edges are about 22 Hz and 22.6 kHz: still inside what a 48 kHz device can carry.
        Assert.True(FrequencyResponse.FrequencyAt(centers, bands - 0.5) < FilterResponse.ReferenceSampleRate / 2);
    }

    [Fact]
    public void A_bypassed_equalizer_is_a_flat_line_whatever_the_settings()
    {
        var engine = EqEngine.Create(15);
        foreach (var b in engine.Bands) b.GainDb = 9;
        engine.BassDb = 12;
        engine.Clarity.Amount = 1;
        engine.Clarity.LowCutHz = 150;
        engine.EnableEqualizer = false;

        Assert.All(FrequencyResponse.Sample(engine), v => Assert.Equal(0, v));
        Assert.Equal(0, FrequencyResponse.GainDb(engine, 1000));
    }

    [Fact]
    public void Corrupt_values_never_produce_a_non_finite_point()
    {
        var engine = EqEngine.Create(15);
        engine.Bands[3].GainDb = double.NaN;
        engine.Bands[4].GainDb = double.PositiveInfinity;
        engine.BassDb = double.NaN;
        engine.Clarity.Amount = double.NaN;
        engine.Clarity.LowCutHz = double.NegativeInfinity;

        Assert.All(FrequencyResponse.Sample(engine), v => Assert.True(double.IsFinite(v)));
    }

    [Fact]
    public void An_engine_with_no_usable_bands_still_yields_a_finite_curve()
    {
        var engine = new EqEngine { Bands = new List<EqBand> { new(double.NaN, 3), new(-5, 2) } };

        var curve = FrequencyResponse.Sample(engine);

        Assert.NotEmpty(curve);
        Assert.All(curve, v => Assert.True(double.IsFinite(v)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    public void An_odd_or_tiny_point_count_is_refused(int pointsPerBand) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => FrequencyResponse.Sample(new EqEngine(), pointsPerBand));

    [Fact]
    public void The_low_cut_pulls_the_left_of_the_curve_down_and_leaves_the_right_alone()
    {
        var engine = EqEngine.Create(31);
        engine.Clarity.LowCutHz = 80;

        var curve = FrequencyResponse.Sample(engine);
        var centers = BandLayouts.Frequencies(31);
        double At(double hz) => curve[8 * Array.IndexOf(centers, hz) + 4];   // the sample at a band's center

        Assert.True(At(20) < -20, $"20 Hz should be far down, got {At(20):0.0} dB");
        Assert.InRange(At(80), -3.2, -2.8);                                   // the 80 Hz band sits on the corner: -3 dB
        Assert.InRange(At(630), -0.02, 0.0);                                  // 630 Hz and up: untouched
        Assert.InRange(curve[^1], -0.02, 0.0);
    }

    [Fact]
    public void The_bass_shelf_lifts_the_bottom_and_the_treble_shelf_lifts_the_top_of_the_curve()
    {
        var engine = EqEngine.Create(15);
        engine.BassDb = 6;
        engine.TrebleDb = 6;

        var curve = FrequencyResponse.Sample(engine);
        var centers = BandLayouts.Frequencies(15);
        double At(double hz) => curve[8 * Array.IndexOf(centers, hz) + 4];

        Assert.InRange(At(25), 5.0, 6.8);
        Assert.InRange(At(1000), -0.5, 1.0);                     // 1 kHz is clear of both shelves
        Assert.InRange(At(16000), 4.5, 6.8);
    }
}

/// <summary>
/// The strongest check on the response curve: render real configs with the generator, read the text back with an
/// independent implementation (parsing the file, complex-number evaluation of each biquad written from the Audio EQ
/// Cookbook, Equalizer APO's documented GraphicEQ rule) and require the curve to agree. What is drawn is then what the
/// file says, whatever the plan or the formulas inside the app do.
/// </summary>
public class ResponseMatchesTheConfigFileTests
{
    private static readonly double[] Probes =
        { 20, 25, 31.5, 40, 55, 80, 100, 125, 160, 250, 400, 630, 1000, 1600, 2500, 3200, 4000, 6300, 8000, 10000, 12500, 16000, 20000 };

    public static IEnumerable<object[]> Cases()
    {
        foreach (var preset in PresetLibrary.BuiltIns)
        foreach (var bands in BandLayouts.SupportedCounts)
            yield return new object[] { preset.Name, bands };
    }

    private static EqEngine Variant(EqEngine e, int variant)
    {
        e.Surround.Mode = SurroundMode.Off;
        switch (variant)
        {
            case 1: e.Clarity.Amount = 0.6; e.Clarity.LowCutHz = 80; break;
            case 2: e.Clarity.Amount = 1.0; e.BassDb = 3.5; e.TrebleDb = -2.5; break;
            case 3: e.Clarity.LowCutHz = 150; e.BassDb = -6; e.TrebleDb = 12; break;
            case 4: e.Clarity.Amount = 0.2; e.Clarity.LowCutHz = 30; e.BassDb = 12; e.TrebleDb = -12; break;
        }
        return e;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void The_drawn_response_equals_the_response_of_the_written_config(string presetName, int bands)
    {
        var preset = PresetLibrary.BuiltIns.Single(p => p.Name == presetName);

        for (int variant = 0; variant <= 4; variant++)
        {
            var engine = Variant(preset.Engine.WithBandCount(bands), variant);
            var config = EqualizerApoConfigGenerator.Generate(engine);

            foreach (double hz in Probes)
                Assert.Equal(ApoReference.GainDb(config, hz), FrequencyResponse.GainDb(engine, hz), 6);

            // The sampled curve uses the same model: check it at its own frequencies too.
            var centers = engine.Bands.Select(b => b.FrequencyHz).ToArray();
            var curve = FrequencyResponse.Sample(engine);
            for (int j = 0; j < curve.Length; j += 5)
            {
                double hz = FrequencyResponse.FrequencyAt(centers, -0.5 + j / 8.0);
                Assert.Equal(ApoReference.GainDb(config, hz), curve[j], 6);
            }
        }
    }

    [Fact]
    public void The_reference_reads_the_config_the_way_the_documentation_says()
    {
        // Straight lines in dB over log-frequency between GraphicEQ points, the end gains held outside them.
        const string config = "GraphicEQ: 100 0.0; 400 6.0; 1600 -6.0\r\n";

        Assert.Equal(0, ApoReference.GainDb(config, 20), 9);
        Assert.Equal(0, ApoReference.GainDb(config, 100), 9);
        Assert.Equal(3, ApoReference.GainDb(config, 200), 9);       // half way, in log frequency, from 100 to 400
        Assert.Equal(6, ApoReference.GainDb(config, 400), 9);
        Assert.Equal(0, ApoReference.GainDb(config, 800), 9);       // half way from +6 to -6
        Assert.Equal(-6, ApoReference.GainDb(config, 20000), 9);
    }

    [Fact]
    public void The_reference_agrees_with_the_cookbook_on_a_filter_it_can_check_by_hand()
    {
        // A peaking filter has exactly its gain at its center frequency.
        Assert.Equal(-2.5, ApoReference.GainDb("Filter 1: ON PK Fc 300 Hz Gain -2.5 dB Q 0.9\r\n", 300), 9);
        // A default high-pass is 3.01 dB down at its corner.
        Assert.Equal(-3.0103, ApoReference.GainDb("Filter 1: ON HP Fc 80 Hz\r\n", 80), 4);
    }

    /// <summary>Reads Equalizer APO config text and evaluates it, sharing no code with the app.</summary>
    private static class ApoReference
    {
        private const double SampleRate = 48000;

        private static readonly Regex FilterLine = new(
            @"^Filter \d+: ON (?<type>[A-Z]+) Fc (?<fc>[-+0-9.]+) Hz(?: Gain (?<gain>[-+0-9.]+) dB)?(?: Q (?<q>[-+0-9.]+))?$",
            RegexOptions.CultureInvariant);

        public static double GainDb(string config, double hz)
        {
            double total = 0;
            foreach (var raw in config.Split('\n'))
            {
                var line = raw.Trim();
                if (line.StartsWith("GraphicEQ:", StringComparison.Ordinal))
                    total += Graphic(line["GraphicEQ:".Length..], hz);
                else if (FilterLine.Match(line) is { Success: true } m)
                    total += Filter(m, hz);
            }
            return total;
        }

        private static double Graphic(string nodesText, double hz)
        {
            var numbers = Regex.Matches(nodesText, @"[-+0-9.eE]+")
                .Select(m => double.Parse(m.Value, CultureInfo.InvariantCulture)).ToArray();
            var nodes = Enumerable.Range(0, numbers.Length / 2)
                .Select(i => (Hz: numbers[2 * i], Db: numbers[2 * i + 1])).OrderBy(n => n.Hz).ToArray();

            if (hz <= nodes[0].Hz) return nodes[0].Db;
            if (hz > nodes[^1].Hz) return nodes[^1].Db;

            int right = Array.FindIndex(nodes, n => n.Hz >= hz);
            var (hzL, dbL) = nodes[right - 1];
            var (hzR, dbR) = nodes[right];
            double t = (Math.Log(hz) - Math.Log(hzL)) / (Math.Log(hzR) - Math.Log(hzL));
            return dbL + t * (dbR - dbL);
        }

        private static double Filter(Match m, double hz)
        {
            string type = m.Groups["type"].Value;
            double fc = double.Parse(m.Groups["fc"].Value, CultureInfo.InvariantCulture);
            double gain = m.Groups["gain"].Success ? double.Parse(m.Groups["gain"].Value, CultureInfo.InvariantCulture) : 0;
            double q = m.Groups["q"].Success ? double.Parse(m.Groups["q"].Value, CultureInfo.InvariantCulture) : Math.Sqrt(0.5);

            double w0 = 2 * Math.PI * fc / SampleRate;
            double alpha = Math.Sin(w0) / (2 * q);
            double cos = Math.Cos(w0);
            double a = Math.Pow(10, gain / 40);
            double twoSqrtAAlpha = 2 * Math.Sqrt(a) * alpha;

            double[] b, d; // numerator b0..b2, denominator a0..a2
            switch (type)
            {
                case "PK":
                    b = new[] { 1 + alpha * a, -2 * cos, 1 - alpha * a };
                    d = new[] { 1 + alpha / a, -2 * cos, 1 - alpha / a };
                    break;
                case "LSC":
                    b = new[] { a * ((a + 1) - (a - 1) * cos + twoSqrtAAlpha), 2 * a * ((a - 1) - (a + 1) * cos), a * ((a + 1) - (a - 1) * cos - twoSqrtAAlpha) };
                    d = new[] { (a + 1) + (a - 1) * cos + twoSqrtAAlpha, -2 * ((a - 1) + (a + 1) * cos), (a + 1) + (a - 1) * cos - twoSqrtAAlpha };
                    break;
                case "HSC":
                    b = new[] { a * ((a + 1) + (a - 1) * cos + twoSqrtAAlpha), -2 * a * ((a - 1) + (a + 1) * cos), a * ((a + 1) + (a - 1) * cos - twoSqrtAAlpha) };
                    d = new[] { (a + 1) - (a - 1) * cos + twoSqrtAAlpha, 2 * ((a - 1) - (a + 1) * cos), (a + 1) - (a - 1) * cos - twoSqrtAAlpha };
                    break;
                case "HP":
                    b = new[] { (1 + cos) / 2, -(1 + cos), (1 + cos) / 2 };
                    d = new[] { 1 + alpha, -2 * cos, 1 - alpha };
                    break;
                default:
                    throw new InvalidOperationException($"The app wrote a filter type this test does not know: {type}");
            }

            // H(z) at z = e^(j w), straight from the polynomials.
            double w = 2 * Math.PI * hz / SampleRate;
            Complex z1 = Complex.FromPolarCoordinates(1, -w);
            Complex z2 = z1 * z1;
            Complex h = (b[0] + b[1] * z1 + b[2] * z2) / (d[0] + d[1] * z1 + d[2] * z2);
            return 20 * Math.Log10(h.Magnitude);
        }
    }
}

public class LimiterCoversTheResponseTests
{
    /// <summary>
    /// With an accurate response model the limiter's peak estimate can be checked against the real highest point of
    /// the curve. For every built-in preset on every layout the estimate must be at least that high, so the trim it
    /// computes really does keep the preset under the ceiling. (The curve is sampled, so the highest sample is a lower
    /// bound on the true peak: a failure here is always real.)
    /// </summary>
    [Fact]
    public void The_estimated_peak_is_never_below_the_curve_for_any_built_in_preset()
    {
        foreach (var preset in PresetLibrary.BuiltIns)
        foreach (var bands in BandLayouts.SupportedCounts)
        {
            var engine = preset.Engine.WithBandCount(bands);

            double estimate = AutoGainLimiter.EstimatePeakBoostDb(engine) - engine.PreampDb;   // the shape's boost, preamp aside
            double highest = Math.Max(0, FrequencyResponse.Sample(engine).Max());

            Assert.True(estimate >= highest - 1e-9,
                $"{preset.Name} on {bands} bands: the limiter reserves {estimate:0.0} dB but the curve reaches {highest:0.0} dB");
        }
    }

    [Fact]
    public void Clarity_is_covered_exactly_by_the_limiter_with_nothing_else_going_on()
    {
        var engine = EqEngine.Create(15);
        engine.Clarity.Amount = 1.0;

        double highest = FrequencyResponse.Sample(engine).Max();

        // The presence peak is +2.0 dB; the limiter budgets exactly that (plus nothing from the bands).
        Assert.Equal(AutoGainLimiter.EstimatePeakBoostDb(engine), highest, 1);
    }
}

public class FilterPlanTests
{
    [Fact]
    public void A_default_engine_needs_no_filters() =>
        Assert.Empty(EqFilterPlan.Create(new EqEngine()));

    [Fact]
    public void The_plan_lists_tone_then_clarity_then_the_low_cut_in_the_order_the_file_uses()
    {
        var engine = new EqEngine { BassDb = 2, TrebleDb = -1 };
        engine.Clarity.Amount = 0.5;
        engine.Clarity.LowCutHz = 70;

        var plan = EqFilterPlan.Create(engine);

        Assert.Equal(new[] { "LSC", "HSC", "PK", "PK", "HSC", "HP" }, plan.Select(f => f.Keyword).ToArray());
        Assert.Equal(new double?[] { 2, -1, -1.25, 1.0, 0.75, null }, plan.Select(f => f.GainDb).ToArray());
        Assert.Equal(new[] { 150.0, 6500, 300, 3200, 10000, 70 }, plan.Select(f => f.FcHz).ToArray());
        Assert.Equal(2, plan.Count(f => f.GroupHeader is not null));   // one header for Clarity, one for the Low-cut
    }

    [Fact]
    public void Switched_off_controls_leave_the_plan_empty()
    {
        var engine = new EqEngine { BassDb = 0.0004, TrebleDb = -0.0004 };
        engine.Clarity.Amount = 0.004;
        engine.Clarity.LowCutHz = 29.9;

        Assert.Empty(EqFilterPlan.Create(engine));
    }

    [Fact]
    public void The_low_cut_corner_is_kept_inside_its_range()
    {
        var engine = new EqEngine();
        engine.Clarity.LowCutHz = 9999;

        Assert.Equal(ClaritySettings.MaxLowCutHz, EqFilterPlan.Create(engine).Single().FcHz);
    }
}
