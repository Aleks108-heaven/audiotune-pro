using AudioTunePro.Core.Models;
using AudioTunePro.Core.Services;

namespace AudioTunePro.Core.Tests;

public sealed class ExternalApoConfigTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "atp-ext-" + Guid.NewGuid().ToString("N"));

    public ExternalApoConfigTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string Write(string name, string text)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public void Missing_file_means_nothing_extra()
    {
        Assert.Equal(ExternalConfigInfo.None, ExternalApoConfig.Scan(Path.Combine(_dir, "config.txt")));
    }

    [Fact]
    public void Preamp_lines_are_summed()
    {
        var config = Write("config.txt", "Preamp: 2.7 dB\r\nPreamp: -1 dB\r\n");

        Assert.Equal(1.7, ExternalApoConfig.Scan(config).PreampDb, 3);
    }

    [Theory]
    [InlineData("Preamp: 3 dB", 3.0)]
    [InlineData("  preamp:+3.5dB", 3.5)]
    [InlineData("Preamp: -4,5 dB", -4.5)]   // decimal comma
    [InlineData("Preamp: 0 dB", 0.0)]
    public void Preamp_formats_are_understood(string line, double expected)
    {
        Assert.Equal(expected, ExternalApoConfig.Scan(Write("config.txt", line)).PreampDb, 3);
    }

    [Theory]
    [InlineData("# Preamp: 6 dB")]
    [InlineData("Preamp: loud")]
    [InlineData("Preamp:")]
    [InlineData("Preamp: --3 dB")]
    [InlineData("")]
    public void Comments_and_garbage_add_nothing(string line)
    {
        Assert.Equal(0.0, ExternalApoConfig.Scan(Write("config.txt", line)).PreampDb);
    }

    [Fact]
    public void AudioTune_Pros_own_include_file_is_never_counted()
    {
        Write("AudioTunePro.txt", "Preamp: 9 dB");
        var config = Write("config.txt", "Preamp: 2 dB\r\nInclude: AudioTunePro.txt\r\n");

        Assert.Equal(2.0, ExternalApoConfig.Scan(config).PreampDb, 3);
    }

    [Fact]
    public void Other_included_files_next_to_config_are_followed()
    {
        Write("mine.txt", "Preamp: 1.5 dB");
        var config = Write("config.txt", "Include: mine.txt\r\nPreamp: 1 dB");

        Assert.Equal(2.5, ExternalApoConfig.Scan(config).PreampDb, 3);
    }

    [Fact]
    public void Includes_outside_the_config_folder_are_not_followed()
    {
        var elsewhere = Path.Combine(Path.GetTempPath(), "atp-out-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(elsewhere, "Preamp: 20 dB");
        try
        {
            var config = Write("config.txt", $"Include: {elsewhere}\r\nInclude: ..\\x.txt\r\nInclude: sub/y.txt");

            Assert.Equal(0.0, ExternalApoConfig.Scan(config).PreampDb);
        }
        finally { File.Delete(elsewhere); }
    }

    [Fact]
    public void Include_loops_terminate()
    {
        Write("a.txt", "Preamp: 1 dB\r\nInclude: b.txt");
        Write("b.txt", "Preamp: 1 dB\r\nInclude: a.txt");
        var config = Write("config.txt", "Include: a.txt");

        Assert.Equal(2.0, ExternalApoConfig.Scan(config).PreampDb, 3); // each file counted once
    }

    [Fact]
    public void VST_plugin_lines_are_flagged()
    {
        var config = Write("config.txt", "VSTPlugin: Library \"C:\\x\\y.dll\"");

        Assert.True(ExternalApoConfig.Scan(config).LoadsPlugins);
        Assert.False(ExternalApoConfig.Scan(Write("c2.txt", "# VSTPlugin: Library x")).LoadsPlugins);
    }

    [Fact]
    public void An_oversized_file_is_ignored()
    {
        var path = Path.Combine(_dir, "config.txt");
        using (var f = File.Create(path)) f.SetLength(2 * 1024 * 1024);

        Assert.Equal(ExternalConfigInfo.None, ExternalApoConfig.Scan(path));
    }
}

public class LimiterExternalGainTests
{
    private static EqEngine BoostedEngine()
    {
        var engine = new EqEngine { PreampDb = 1.3 };
        engine.Bands[0].GainDb = 5.5;
        engine.Limiter.AutoGainProtection = true;
        engine.Limiter.CeilingDb = -0.3;
        return engine;
    }

    [Fact]
    public void External_gain_raises_the_estimated_peak_one_for_one()
    {
        var engine = BoostedEngine();

        Assert.Equal(AutoGainLimiter.EstimatePeakBoostDb(engine) + 2.7,
                     AutoGainLimiter.EstimatePeakBoostDb(engine, 2.7), 6);
    }

    [Fact]
    public void Protection_trims_by_the_external_gain_too()
    {
        var engine = BoostedEngine();

        double without = AutoGainLimiter.ComputeTrimDb(engine);
        double with = AutoGainLimiter.ComputeTrimDb(engine, 2.7);

        Assert.Equal(without - 2.7, with, 6);
    }

    [Fact]
    public void Total_gain_including_the_external_preamp_stays_under_the_ceiling()
    {
        var engine = BoostedEngine();
        const double external = 2.7;

        double applied = engine.PreampDb + AutoGainLimiter.ComputeTrimDb(engine, external) + external;
        double curvePeak = AutoGainLimiter.EstimatePeakBoostDb(engine) - engine.PreampDb;

        Assert.True(curvePeak + applied <= engine.Limiter.CeilingDb + 1e-9);
    }

    [Fact]
    public void Protection_off_never_trims_regardless_of_external_gain()
    {
        var engine = BoostedEngine();
        engine.Limiter.AutoGainProtection = false;

        Assert.Equal(0.0, AutoGainLimiter.ComputeTrimDb(engine, 6));
        Assert.Equal(0.0, EqualizerApoConfigGenerator.TotalTrimDb(engine, 6));
    }

    [Fact]
    public void A_negative_external_preamp_leaves_headroom_so_less_trim_is_needed()
    {
        var engine = BoostedEngine();

        Assert.True(AutoGainLimiter.ComputeTrimDb(engine, -6) > AutoGainLimiter.ComputeTrimDb(engine));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_non_finite_external_gain_is_ignored(double bad)
    {
        var engine = BoostedEngine();

        Assert.Equal(AutoGainLimiter.EstimatePeakBoostDb(engine), AutoGainLimiter.EstimatePeakBoostDb(engine, bad));
    }

    [Fact]
    public void Generated_config_with_external_gain_writes_the_lower_preamp_and_says_why()
    {
        var engine = BoostedEngine();

        var without = EqualizerApoConfigGenerator.Generate(engine);
        var with = EqualizerApoConfigGenerator.Generate(engine, 2.7);

        Assert.Contains("Preamp: ", with);
        Assert.NotEqual(without, with);
        Assert.Contains("rest of config.txt", with);
    }

    [Fact]
    public void Default_behaviour_without_external_gain_is_unchanged()
    {
        var engine = BoostedEngine();

        Assert.Equal(EqualizerApoConfigGenerator.Generate(engine, 0).Split('\n').Skip(3),
                     EqualizerApoConfigGenerator.Generate(engine).Split('\n').Skip(3));
    }
}
