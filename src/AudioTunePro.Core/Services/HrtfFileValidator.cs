using System.Text;

namespace AudioTunePro.Core.Services;

/// <summary>
/// Sanity-checks an HRTF impulse-response file before its path goes into the Equalizer APO config.
/// Windows' audio service (not this app) opens and parses the file, and it often sits in a folder any program
/// running as the user can write to, so the app only hands over a small, well-formed RIFF/WAVE file:
/// not a link, a plausible audio format, and short enough that convolving it cannot starve the audio engine.
/// This narrows the risk; it cannot remove it (the file could still be swapped after the check).
/// </summary>
public static class HrtfFileValidator
{
    public const long MaxFileBytes = 4 * 1024 * 1024;

    /// <summary>~1.5 s at 44.1 kHz. Real HRTFs are a few hundred to a few thousand samples.</summary>
    public const long MaxFrames = 65_536;

    public const int MaxChannels = 16;

    public static bool TryValidate(string path, out string error)
    {
        error = string.Empty;
        try
        {
            if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
                return Fail("it is a link, not the file itself", out error);

            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (fs.Length > MaxFileBytes)
                return Fail($"it is larger than {MaxFileBytes / (1024 * 1024)} MB", out error);
            if (fs.Length < 12)
                return Fail("it is not a WAV file", out error);

            using var reader = new BinaryReader(fs, Encoding.ASCII, leaveOpen: true);
            if (ReadId(reader) != "RIFF") return Fail("it is not a WAV file", out error);
            reader.ReadUInt32();
            if (ReadId(reader) != "WAVE") return Fail("it is not a WAV file", out error);

            ushort channels = 0, blockAlign = 0;
            bool haveFormat = false;

            // A real header has a handful of chunks; the cap stops a crafted chain from looping for long.
            for (int chunk = 0; chunk < 64 && fs.Position + 8 <= fs.Length; chunk++)
            {
                var id = ReadId(reader);
                uint size = reader.ReadUInt32();
                long next = fs.Position + size + (size & 1);

                if (id == "fmt ")
                {
                    if (size < 16) return Fail("its format header is damaged", out error);
                    ushort tag = reader.ReadUInt16();
                    channels = reader.ReadUInt16();
                    reader.ReadUInt32(); // sample rate
                    reader.ReadUInt32(); // byte rate
                    blockAlign = reader.ReadUInt16();
                    reader.ReadUInt16(); // bits per sample
                    if (tag is not (1 or 3 or 0xFFFE)) return Fail("it is not PCM or float audio", out error);
                    if (channels is 0 or > MaxChannels) return Fail($"it has {channels} channels", out error);
                    if (blockAlign == 0) return Fail("its format header is damaged", out error);
                    haveFormat = true;
                }
                else if (id == "data")
                {
                    if (!haveFormat) return Fail("its format header is missing", out error);
                    long dataBytes = Math.Min(size, fs.Length - fs.Position);
                    long frames = dataBytes / blockAlign;
                    if (frames == 0) return Fail("it contains no audio", out error);
                    if (frames > MaxFrames) return Fail($"it is {frames:N0} samples long (limit {MaxFrames:N0})", out error);
                    return true;
                }

                if (next > fs.Length) break;
                fs.Position = next;
            }

            return Fail("it contains no audio data", out error);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or EndOfStreamException or ArgumentException or NotSupportedException)
        {
            return Fail("it can't be read", out error);
        }
    }

    private static string ReadId(BinaryReader reader) => Encoding.ASCII.GetString(reader.ReadBytes(4));

    private static bool Fail(string reason, out string error)
    {
        error = reason;
        return false;
    }
}
