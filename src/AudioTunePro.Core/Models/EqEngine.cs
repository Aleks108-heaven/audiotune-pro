namespace AudioTunePro.Core.Models;

/// <summary>
/// The full, live equalizer state: 10-band graphic EQ, bass/treble shelves,
/// master preamp, and limiter settings. This is the single source of truth
/// that gets serialized to disk (as a profile) and rendered to an
/// Equalizer APO config by <see cref="AudioTunePro.Core.Services.EqualizerApoConfigGenerator"/>.
/// </summary>
public sealed class EqEngine
{
    /// <summary>Standard 10-band ISO graphic EQ center frequencies.</summary>
    public static readonly double[] StandardBandFrequencies =
        { 31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 };

    public List<EqBand> Bands { get; set; }

    /// <summary>Low-shelf "Bass" control, in dB, applied below ~150 Hz.</summary>
    public double BassDb { get; set; }

    /// <summary>High-shelf "Treble" control, in dB, applied above ~6 kHz.</summary>
    public double TrebleDb { get; set; }

    /// <summary>Overall master gain in dB, applied before the limiter stage.</summary>
    public double PreampDb { get; set; }

    public bool EnableEqualizer { get; set; } = true;

    public LimiterSettings Limiter { get; set; } = new();

    public SurroundSettings Surround { get; set; } = new();

    public EqEngine()
    {
        Bands = StandardBandFrequencies.Select(f => new EqBand(f)).ToList();
    }

    public EqEngine Clone()
    {
        return new EqEngine
        {
            Bands = Bands.Select(b => b.Clone()).ToList(),
            BassDb = BassDb,
            TrebleDb = TrebleDb,
            PreampDb = PreampDb,
            EnableEqualizer = EnableEqualizer,
            Limiter = Limiter.Clone(),
            Surround = Surround.Clone(),
        };
    }

    /// <summary>Returns a copy with <see cref="SurroundMode.Auto"/> replaced by the mode matching the output device.</summary>
    public EqEngine ResolveSurround(OutputKind output)
    {
        var copy = Clone();
        if (copy.Surround.Mode == SurroundMode.Auto)
            copy.Surround.Mode = output == OutputKind.Headphones ? SurroundMode.Headphones : SurroundMode.Speakers;
        return copy;
    }

    /// <summary>True when both engines would render to the same sound (band gains, tone, preamp, limiter, surround).</summary>
    public bool IsEquivalentTo(EqEngine other)
    {
        if (Bands.Count != other.Bands.Count) return false;
        for (int i = 0; i < Bands.Count; i++)
        {
            if (Bands[i].FrequencyHz != other.Bands[i].FrequencyHz ||
                Math.Abs(Bands[i].GainDb - other.Bands[i].GainDb) > 0.001) return false;
        }

        return Math.Abs(BassDb - other.BassDb) <= 0.001 &&
               Math.Abs(TrebleDb - other.TrebleDb) <= 0.001 &&
               Math.Abs(PreampDb - other.PreampDb) <= 0.001 &&
               EnableEqualizer == other.EnableEqualizer &&
               Limiter.AutoGainProtection == other.Limiter.AutoGainProtection &&
               Math.Abs(Limiter.CeilingDb - other.Limiter.CeilingDb) <= 0.001 &&
               Surround.Mode == other.Surround.Mode &&
               Math.Abs(Surround.Amount - other.Surround.Amount) <= 0.001 &&
               string.Equals(Surround.HrtfFilePath ?? string.Empty, other.Surround.HrtfFilePath ?? string.Empty,
                   StringComparison.Ordinal);
    }

    /// <summary>
    /// Repairs a deserialized (possibly hand-edited or corrupt) engine: exactly the ten standard
    /// bands, every value finite and inside its control's range. Returns a new engine.
    /// </summary>
    public EqEngine Sanitized()
    {
        static double Fix(double v, double min, double max, double fallback = 0) =>
            double.IsFinite(v) ? Math.Clamp(v, min, max) : fallback;

        var result = new EqEngine
        {
            BassDb = Fix(BassDb, -12, 12),
            TrebleDb = Fix(TrebleDb, -12, 12),
            PreampDb = Fix(PreampDb, -24, 12),
            EnableEqualizer = EnableEqualizer,
        };

        for (int i = 0; i < result.Bands.Count; i++)
        {
            double gain = Bands is not null && i < Bands.Count ? Bands[i].GainDb : 0;
            result.Bands[i].GainDb = Fix(gain, -12, 12);
        }

        var limiter = Limiter ?? new LimiterSettings();
        result.Limiter = new LimiterSettings
        {
            AutoGainProtection = limiter.AutoGainProtection,
            CeilingDb = Fix(limiter.CeilingDb, -6, 0, -0.3),
        };

        var surround = Surround ?? new SurroundSettings();
        result.Surround = new SurroundSettings
        {
            Mode = Enum.IsDefined(surround.Mode) ? surround.Mode : SurroundMode.Auto,
            Amount = Fix(surround.Amount, 0, 1, 0.5),
            HrtfFilePath = SurroundSettings.IsSafeHrtfPath(surround.HrtfFilePath) ? surround.HrtfFilePath!.Trim() : null,
        };

        return result;
    }

    public void Reset()
    {
        foreach (var b in Bands) b.GainDb = 0;
        BassDb = 0;
        TrebleDb = 0;
        PreampDb = 0;
    }
}

public sealed class LimiterSettings
{
    /// <summary>
    /// When true, AudioTune Pro automatically trims the master preamp so the sum of
    /// positive EQ boosts can never push the signal into digital clipping (0 dBFS).
    /// This is a static, always-safe form of limiting computed at config-write time.
    /// </summary>
    public bool AutoGainProtection { get; set; } = true;

    /// <summary>
    /// Ceiling in dBFS the auto-gain protection will target as the loudest a fully
    /// constructive boost could reach. Negative headroom below 0 dBFS.
    /// </summary>
    public double CeilingDb { get; set; } = -0.3;

    public LimiterSettings Clone() => new()
    {
        AutoGainProtection = AutoGainProtection,
        CeilingDb = CeilingDb,
    };
}
