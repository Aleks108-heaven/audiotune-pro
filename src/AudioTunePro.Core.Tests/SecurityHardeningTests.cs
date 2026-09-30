using AudioTunePro.Core.Models;
using AudioTunePro.Core.Services;
using Xunit;

namespace AudioTunePro.Core.Tests;

public class HrtfPathSafetyTests
{
    [Theory]
    [InlineData(@"C:\ir\hrtf.wav")]
    [InlineData(@"D:\Music\My IRs\kemar 30deg.WAV")]
    public void Plain_local_wav_paths_are_accepted(string path) =>
        Assert.True(SurroundSettings.IsSafeHrtfPath(path));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("hrtf.wav")]                              // relative: would resolve inside the audio service
    [InlineData(@"..\..\hrtf.wav")]
    [InlineData(@"\\attacker\share\hrtf.wav")]            // UNC: audio service would reach out to the network
    [InlineData(@"\\?\C:\hrtf.wav")]                      // device path
    [InlineData("//attacker/share/hrtf.wav")]
    [InlineData(@"C:\ir\hrtf.wav:hidden")]                // NTFS alternate data stream
    [InlineData(@"C:\ir\hrtf.dll")]                       // not a wav
    [InlineData("C:\\ir\\a.wav\nInclude: evil.txt")]      // config-line injection
    [InlineData("C:\\ir\\a.wav\rInclude: evil.txt")]
    [InlineData("C:\\ir\\a\t.wav")]
    [InlineData(@"C:\ir\*.wav")]
    [InlineData(@"C:\ir\a|b.wav")]
    [InlineData(@"C:hrtf.wav")]                           // drive-relative
    public void Unsafe_paths_are_rejected(string? path) =>
        Assert.False(SurroundSettings.IsSafeHrtfPath(path));

    [Fact]
    public void Overlong_paths_are_rejected() =>
        Assert.False(SurroundSettings.IsSafeHrtfPath(@"C:\" + new string('a', 300) + ".wav"));

    [Fact]
    public void An_unsafe_path_never_reaches_the_generated_config()
    {
        var engine = new EqEngine();
        engine.Surround.Mode = SurroundMode.Headphones;
        engine.Surround.HrtfFilePath = @"\\attacker\share\x.wav";

        var config = EqualizerApoConfigGenerator.Generate(engine);

        Assert.DoesNotContain("attacker", config);
        Assert.DoesNotContain("Convolution", config);
        Assert.Contains("crossfeed", config); // falls back to the built-in crossfeed
    }

    [Fact]
    public void Sanitizing_a_loaded_engine_drops_an_unsafe_hrtf_path()
    {
        var engine = new EqEngine();
        engine.Surround.HrtfFilePath = @"\\attacker\share\x.wav";
        Assert.Null(engine.Sanitized().Surround.HrtfFilePath);

        engine.Surround.HrtfFilePath = @"C:\ir\ok.wav";
        Assert.Equal(@"C:\ir\ok.wav", engine.Sanitized().Surround.HrtfFilePath);
    }
}

public class OversizedDataFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "AudioTuneProSec_" + Guid.NewGuid());

    [Fact]
    public void A_huge_settings_file_is_ignored_rather_than_read_into_memory()
    {
        var store = new AppDataStore(_dir);
        using (var f = File.Create(Path.Combine(_dir, "settings.json")))
            f.SetLength(3 * 1024 * 1024);

        Assert.Equal(new AppSettings().ActivePresetName, store.LoadSettings().ActivePresetName);
    }

    [Fact]
    public void A_huge_presets_file_is_ignored()
    {
        var store = new AppDataStore(_dir);
        using (var f = File.Create(Path.Combine(_dir, "user-presets.json")))
            f.SetLength(3 * 1024 * 1024);

        Assert.Empty(store.LoadUserPresets());
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}
