using AudioTunePro.Core.Services;

namespace AudioTunePro.Core.Tests;

public sealed class ApoConfigWriterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "atp-apo-" + Guid.NewGuid().ToString("N"));

    public ApoConfigWriterTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string MainConfig => Path.Combine(_dir, "config.txt");
    private int IncludeCount() => File.ReadAllLines(MainConfig).Count(l => l.Trim() == "Include: AudioTunePro.txt");

    [Fact]
    public void Missing_config_txt_is_created_with_just_the_include_line()
    {
        ApoConfigWriter.EnsureIncludeLine(MainConfig);

        Assert.Equal(new[] { "Include: AudioTunePro.txt" }, File.ReadAllLines(MainConfig));
    }

    [Fact]
    public void Existing_user_config_is_preserved_and_include_appended_once()
    {
        File.WriteAllText(MainConfig, "Preamp: -3 dB\r\nInclude: mine.txt\r\n");

        ApoConfigWriter.EnsureIncludeLine(MainConfig);
        ApoConfigWriter.EnsureIncludeLine(MainConfig);
        ApoConfigWriter.EnsureIncludeLine(MainConfig);

        var lines = File.ReadAllLines(MainConfig);
        Assert.Equal("Preamp: -3 dB", lines[0]);
        Assert.Equal("Include: mine.txt", lines[1]);
        Assert.Equal(1, IncludeCount());
    }

    [Fact]
    public void Config_without_trailing_newline_gets_the_include_on_its_own_line()
    {
        File.WriteAllText(MainConfig, "Preamp: -3 dB"); // no newline at EOF

        ApoConfigWriter.EnsureIncludeLine(MainConfig);

        var lines = File.ReadAllLines(MainConfig);
        Assert.Equal("Preamp: -3 dB", lines[0]);
        Assert.Equal(1, IncludeCount());
    }

    [Fact]
    public void Include_line_is_recognised_regardless_of_case_and_surrounding_whitespace()
    {
        File.WriteAllText(MainConfig, "  include: audiotunepro.TXT  \r\n");

        ApoConfigWriter.EnsureIncludeLine(MainConfig);

        Assert.Single(File.ReadAllLines(MainConfig));
    }

    [Fact]
    public void Commented_out_include_does_not_count_as_included()
    {
        File.WriteAllText(MainConfig, "# Include: AudioTunePro.txt\r\n");

        ApoConfigWriter.EnsureIncludeLine(MainConfig);

        Assert.Equal(1, IncludeCount());
    }

    [Fact]
    public void Apply_writes_snippet_replaces_it_on_next_apply_and_leaves_no_temp_file()
    {
        ApoConfigWriter.Apply(_dir, "Preamp: -1.00 dB");
        ApoConfigWriter.Apply(_dir, "Preamp: -2.00 dB");

        Assert.Equal("Preamp: -2.00 dB", File.ReadAllText(Path.Combine(_dir, "AudioTunePro.txt")));
        Assert.False(File.Exists(Path.Combine(_dir, "AudioTunePro.txt.tmp")));
        Assert.Equal(1, IncludeCount());
    }

    [Fact]
    public void Apply_creates_the_config_directory_when_missing()
    {
        var nested = Path.Combine(_dir, "a", "config");

        ApoConfigWriter.Apply(nested, "Preamp: 0 dB");

        Assert.True(File.Exists(Path.Combine(nested, "AudioTunePro.txt")));
        Assert.True(File.Exists(Path.Combine(nested, "config.txt")));
    }
}
