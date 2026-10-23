using AudioTunePro.Core.Models;

namespace AudioTunePro.Core.Services;

/// <summary>The kind of biquad an Equalizer APO <c>Filter</c> line asks for.</summary>
public enum FilterShape
{
    Peaking,
    LowShelf,
    HighShelf,
    HighPass,
}

/// <summary>
/// One <c>Filter</c> line the app writes after the <c>GraphicEQ</c> line.
/// </summary>
/// <param name="Keyword">The Equalizer APO type keyword (<c>PK</c>, <c>LSC</c>, <c>HSC</c>, <c>HP</c>).</param>
/// <param name="Shape">What the keyword means mathematically.</param>
/// <param name="FcHz">Center frequency (shelves, peaks) or corner frequency (high-pass).</param>
/// <param name="GainDb">Gain, or null for a filter that has none (high-pass).</param>
/// <param name="Q">Quality factor, or null to leave it to Equalizer APO's default (1/√2 for a high-pass).</param>
/// <param name="GroupHeader">A comment line to write, after a blank line, before the first filter of a group.</param>
public sealed record FilterSpec(string Keyword, FilterShape Shape, double FcHz, double? GainDb, double? Q, string? GroupHeader = null);

/// <summary>
/// The filters an <see cref="EqEngine"/> adds on top of its graphic EQ: Bass and Treble shelves, the Clarity
/// curve and the Low-cut. This single list is what <see cref="EqualizerApoConfigGenerator"/> writes to the config
/// file and what <see cref="FrequencyResponse"/> draws, so the response curve on screen cannot drift from the sound
/// that is actually applied.
/// </summary>
public static class EqFilterPlan
{
    public const double BassFcHz = 150;
    public const double TrebleFcHz = 6500;
    public const double ToneShelfQ = 0.9;

    /// <summary>A Bass/Treble value closer to zero than this is treated as off.</summary>
    public const double MinToneDb = 0.001;

    /// <summary>A Clarity amount below this is treated as off (it would round to a 0.0 dB filter).</summary>
    public const double MinClarityAmount = 0.005;

    public static IReadOnlyList<FilterSpec> Create(EqEngine engine)
    {
        var plan = new List<FilterSpec>();

        if (Math.Abs(engine.BassDb) > MinToneDb)
            plan.Add(new FilterSpec("LSC", FilterShape.LowShelf, BassFcHz, engine.BassDb, ToneShelfQ));
        if (Math.Abs(engine.TrebleDb) > MinToneDb)
            plan.Add(new FilterSpec("HSC", FilterShape.HighShelf, TrebleFcHz, engine.TrebleDb, ToneShelfQ));

        var clarity = engine.Clarity;
        if (clarity is null) return plan;

        double amount = double.IsFinite(clarity.Amount) ? Math.Clamp(clarity.Amount, 0, 1) : 0;
        if (amount >= MinClarityAmount)
        {
            plan.Add(new FilterSpec("PK", FilterShape.Peaking, ClaritySettings.MudFcHz, amount * ClaritySettings.MudCutDb,
                ClaritySettings.MudQ,
                FormattableString.Invariant($"# Clarity {amount:P0}: low-mid cut, speech presence, air")));
            plan.Add(new FilterSpec("PK", FilterShape.Peaking, ClaritySettings.PresenceFcHz, amount * ClaritySettings.PresenceBoostDb,
                ClaritySettings.PresenceQ));
            plan.Add(new FilterSpec("HSC", FilterShape.HighShelf, ClaritySettings.AirFcHz, amount * ClaritySettings.AirBoostDb,
                ClaritySettings.AirQ));
        }

        if (ClaritySettings.IsLowCutOn(clarity.LowCutHz))
        {
            double hz = Math.Clamp(clarity.LowCutHz, ClaritySettings.MinLowCutHz, ClaritySettings.MaxLowCutHz);
            plan.Add(new FilterSpec("HP", FilterShape.HighPass, hz, null, null,
                "# Low-cut: removes bass the speakers cannot reproduce (less distortion)"));
        }

        return plan;
    }
}
