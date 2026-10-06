using System.Text;
using AudioTunePro.Core.Services;

namespace AudioTunePro.Core.Tests;

public sealed class HrtfFileValidatorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "atp-hrtf-" + Guid.NewGuid().ToString("N"));

    public HrtfFileValidatorTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    /// <summary>Builds a minimal PCM WAV; <paramref name="frames"/> is declared in the header, the data is real.</summary>
    private string Wav(string name, ushort channels = 2, int frames = 256, ushort tag = 1, bool dataBeforeFmt = false, bool declareFrames = true)
    {
        ushort blockAlign = (ushort)(channels * 2);
        int dataBytes = frames * blockAlign;
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.ASCII);

        void Fmt()
        {
            w.Write(Encoding.ASCII.GetBytes("fmt "));
            w.Write(16u);
            w.Write(tag); w.Write(channels); w.Write(44100u); w.Write((uint)(44100 * blockAlign)); w.Write(blockAlign); w.Write((ushort)16);
        }
        void Data()
        {
            w.Write(Encoding.ASCII.GetBytes("data"));
            w.Write(declareFrames ? (uint)dataBytes : 0u);
            w.Write(new byte[dataBytes]); // a real body: the validator trusts the bytes present, not just the header's claim
        }

        w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(0u); w.Write(Encoding.ASCII.GetBytes("WAVE"));
        if (dataBeforeFmt) { Data(); Fmt(); } else { Fmt(); Data(); }

        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, ms.ToArray());
        return path;
    }

    [Theory]
    [InlineData(1, 128)]
    [InlineData(2, 256)]
    [InlineData(4, 512)]
    [InlineData(2, 65_536)]   // exactly at the limit
    public void Small_well_formed_wavs_pass(ushort channels, int frames)
    {
        Assert.True(HrtfFileValidator.TryValidate(Wav("ok.wav", channels, frames), out var error), error);
    }

    [Fact]
    public void Float_and_extensible_formats_pass()
    {
        Assert.True(HrtfFileValidator.TryValidate(Wav("f.wav", tag: 3), out _));
        Assert.True(HrtfFileValidator.TryValidate(Wav("e.wav", tag: 0xFFFE), out _));
    }

    [Fact]
    public void A_wav_longer_than_the_sample_limit_is_rejected()
    {
        Assert.False(HrtfFileValidator.TryValidate(Wav("long.wav", 2, 65_537), out var error));
        Assert.Contains("long", error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    public void Implausible_channel_counts_are_rejected(ushort channels)
    {
        Assert.False(HrtfFileValidator.TryValidate(Wav("ch.wav", channels), out _));
    }

    [Fact]
    public void Compressed_formats_are_rejected()
    {
        Assert.False(HrtfFileValidator.TryValidate(Wav("mp3.wav", tag: 0x55), out _));
    }

    [Fact]
    public void Data_before_format_and_empty_data_are_rejected()
    {
        Assert.False(HrtfFileValidator.TryValidate(Wav("order.wav", dataBeforeFmt: true), out _));
        Assert.False(HrtfFileValidator.TryValidate(Wav("empty.wav", declareFrames: false), out _));
    }

    [Fact]
    public void A_header_that_claims_more_audio_than_the_file_holds_is_judged_by_the_bytes_present()
    {
        // Declares 1,000,000 frames but carries only 256: what the audio service can actually read is small.
        var path = Wav("liar.wav", 2, 256);
        var bytes = File.ReadAllBytes(path);
        BitConverter.GetBytes(4_000_000u).CopyTo(bytes, 40); // data chunk size field
        File.WriteAllBytes(path, bytes);

        Assert.True(HrtfFileValidator.TryValidate(path, out _));
    }

    [Fact]
    public void Non_wav_content_is_rejected_even_with_a_wav_name()
    {
        var path = Path.Combine(_dir, "fake.wav");
        File.WriteAllText(path, "MZ this is not audio at all, just text pretending");

        Assert.False(HrtfFileValidator.TryValidate(path, out var error));
        Assert.Contains("not a WAV", error);
    }

    [Fact]
    public void Truncated_and_tiny_files_are_rejected_without_throwing()
    {
        var tiny = Path.Combine(_dir, "tiny.wav");
        File.WriteAllBytes(tiny, new byte[] { 1, 2, 3 });
        Assert.False(HrtfFileValidator.TryValidate(tiny, out _));

        var cut = Path.Combine(_dir, "cut.wav");
        File.WriteAllBytes(cut, File.ReadAllBytes(Wav("full.wav")).Take(20).ToArray());
        Assert.False(HrtfFileValidator.TryValidate(cut, out _));
    }

    [Fact]
    public void A_file_over_the_size_cap_is_rejected_before_it_is_parsed()
    {
        var path = Path.Combine(_dir, "big.wav");
        using (var f = File.Create(path)) f.SetLength(HrtfFileValidator.MaxFileBytes + 1);

        Assert.False(HrtfFileValidator.TryValidate(path, out var error));
        Assert.Contains("larger", error);
    }

    [Fact]
    public void A_missing_file_is_rejected()
    {
        Assert.False(HrtfFileValidator.TryValidate(Path.Combine(_dir, "nope.wav"), out var error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void A_symbolic_link_is_rejected()
    {
        var target = Wav("real.wav");
        var link = Path.Combine(_dir, "link.wav");
        try { File.CreateSymbolicLink(link, target); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return; // creating links needs a privilege this account may not have; nothing to check then
        }

        Assert.False(HrtfFileValidator.TryValidate(link, out var error));
        Assert.Contains("link", error);
    }
}
