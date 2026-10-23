namespace AudioTunePro.Core.Models;

/// <summary>
/// The full, live equalizer state: graphic EQ (10, 15 or 31 bands, see <see cref="BandLayouts"/>), bass/treble
/// shelves, clarity filters, master preamp, and limiter settings. This is the single source of truth
/// that gets serialized to disk (as a profile) and rendered to an
/// Equalizer APO config by <see cref="AudioTunePro.Core.Services.EqualizerApoConfigGenerator"/>.
/// </summary>
public sealed class EqEngine
{
    /// <summary>Standard 10-band octave graphic EQ center frequencies (the original layout, see <see cref="BandLayouts.Octave"/>).</summary>
    public static readonly double[] StandardBandFrequencies = BandLayouts.Frequencies(BandLayouts.Octave);

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

    /// <summary>Clarity curve and low-cut filter. Absent in files saved by older versions, which then load as off.</summary>
    public ClaritySettings Clarity { get; set; } = new();

    public EqEngine()
    {
        Bands = StandardBandFrequencies.Select(f => new EqBand(f)).ToList();
    }

    /// <summary>A flat engine on one of the supported band layouts.</summary>
    public static EqEngine Create(int bandCount) => new()
    {
        Bands = BandLayouts.Frequencies(bandCount).Select(f => new EqBand(f)).ToList(),
    };

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
            Clarity = (Clarity ?? new ClaritySettings()).Clone(),
        };
    }

    /// <summary>
    /// Returns a copy expressed on the <paramref name="bandCount"/>-band layout. The curve is read off the current
    /// bands by straight-line interpolation over log-frequency (holding the end values beyond the first and last
    /// band), which is how a <c>GraphicEQ</c> line is drawn between its points, so the sound stays essentially the
    /// same. Going to fewer bands smooths fine detail that the coarser layout cannot hold. Always a new object.
    /// </summary>
    public EqEngine WithBandCount(int bandCount)
    {
        var target = BandLayouts.Frequencies(bandCount);
        var copy = Clone();

        bool sameLayout = copy.Bands.Count == target.Length;
        for (int i = 0; sameLayout && i < target.Length; i++)
            sameLayout = copy.Bands[i].FrequencyHz == target[i];
        if (sameLayout) return copy;

        var source = copy.Bands
            .Where(b => double.IsFinite(b.FrequencyHz) && b.FrequencyHz > 0)
            .OrderBy(b => b.FrequencyHz)
            .ToArray();
        copy.Bands = target
            .Select(f => new EqBand(f, Math.Clamp(Math.Round(GainAt(source, f), 1), -12, 12)))
            .ToList();
        return copy;
    }

    private static double GainAt(EqBand[] sorted, double hz)
    {
        static double Gain(EqBand b) => double.IsFinite(b.GainDb) ? b.GainDb : 0;

        if (sorted.Length == 0) return 0;
        if (hz <= sorted[0].FrequencyHz) return Gain(sorted[0]);
        if (hz >= sorted[^1].FrequencyHz) return Gain(sorted[^1]);

        for (int i = 1; i < sorted.Length; i++)
        {
            double hi = sorted[i].FrequencyHz;
            if (hz > hi) continue;

            double lo = sorted[i - 1].FrequencyHz;
            if (hi <= lo) return Gain(sorted[i]);
            double t = (Math.Log(hz) - Math.Log(lo)) / (Math.Log(hi) - Math.Log(lo));
            return Gain(sorted[i - 1]) + (Gain(sorted[i]) - Gain(sorted[i - 1])) * t;
        }

        return Gain(sorted[^1]);
    }

    /// <summary>Returns a copy with <see cref="SurroundMode.Auto"/> replaced by the mode matching the output device.</summary>
    public EqEngine ResolveSurround(OutputKind output)
    {
        var copy = Clone();
        if (copy.Surround.Mode == SurroundMode.Auto)
            copy.Surround.Mode = output == OutputKind.Headphones ? SurroundMode.Headphones : SurroundMode.Speakers;
        return copy;
    }

    /// <summary>True when both engines would render to the same sound (band gains, tone, clarity, preamp, limiter, surround).</summary>
    public bool IsEquivalentTo(EqEngine other)
    {
        if (Bands.Count != other.Bands.Count) return false;
        for (int i = 0; i < Bands.Count; i++)
        {
            if (Bands[i].FrequencyHz != other.Bands[i].FrequencyHz ||
                Math.Abs(Bands[i].GainDb - other.Bands[i].GainDb) > 0.001) return false;
        }

        var clarity = Clarity ?? new ClaritySettings();
        var otherClarity = other.Clarity ?? new ClaritySettings();

        return Math.Abs(BassDb - other.BassDb) <= 0.001 &&
               Math.Abs(TrebleDb - other.TrebleDb) <= 0.001 &&
               Math.Abs(PreampDb - other.PreampDb) <= 0.001 &&
               Math.Abs(clarity.Amount - otherClarity.Amount) <= 0.001 &&
               Math.Abs(EffectiveLowCutHz(clarity) - EffectiveLowCutHz(otherClarity)) <= 0.001 &&
               EnableEqualizer == other.EnableEqualizer &&
               Limiter.AutoGainProtection == other.Limiter.AutoGainProtection &&
               Math.Abs(Limiter.CeilingDb - other.Limiter.CeilingDb) <= 0.001 &&
               Surround.Mode == other.Surround.Mode &&
               Math.Abs(Surround.Amount - other.Surround.Amount) <= 0.001 &&
               string.Equals(Surround.HrtfFilePath ?? string.Empty, other.Surround.HrtfFilePath ?? string.Empty,
                   StringComparison.Ordinal);
    }

    private static double EffectiveLowCutHz(ClaritySettings c) => ClaritySettings.IsLowCutOn(c.LowCutHz) ? c.LowCutHz : 0;

    /// <summary>
    /// Repairs a deserialized (possibly hand-edited or corrupt) engine: one of the supported band layouts
    /// (anything else falls back to the ten standard bands), every value finite and inside its control's range.
    /// Returns a new engine.
    /// </summary>
    public EqEngine Sanitized()
    {
        static double Fix(double v, double min, double max, double fallback = 0) =>
            double.IsFinite(v) ? Math.Clamp(v, min, max) : fallback;

        int bandCount = Bands is not null && BandLayouts.IsSupported(Bands.Count) ? Bands.Count : BandLayouts.Octave;

        var result = Create(bandCount);
        result.BassDb = Fix(BassDb, -12, 12);
        result.TrebleDb = Fix(TrebleDb, -12, 12);
        result.PreampDb = Fix(PreampDb, -24, 12);
        result.EnableEqualizer = EnableEqualizer;

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

        var clarity = Clarity ?? new ClaritySettings();
        result.Clarity = new ClaritySettings
        {
            Amount = Fix(clarity.Amount, 0, 1),
            LowCutHz = ClaritySettings.IsLowCutOn(clarity.LowCutHz)
                ? Math.Clamp(clarity.LowCutHz, ClaritySettings.MinLowCutHz, ClaritySettings.MaxLowCutHz)
                : 0,
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

    /// <summary>Back to a flat, uncolored sound: every band, Bass, Treble, Preamp, Clarity and Low-cut at 0 / off.</summary>
    public void Reset()
    {
        foreach (var b in Bands) b.GainDb = 0;
        BassDb = 0;
        TrebleDb = 0;
        PreampDb = 0;
        Clarity = new ClaritySettings();
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
