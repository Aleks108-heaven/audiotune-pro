namespace AudioTunePro.Core.Models;

/// <summary>
/// A single graphic-EQ band: a center frequency in Hz and a gain in dB.
/// Q is fixed per band by <see cref="EqEngine"/> so bands overlap smoothly (ISO-style graphic EQ).
/// </summary>
public sealed class EqBand
{
    public double FrequencyHz { get; init; }
    public double GainDb { get; set; }

    public EqBand(double frequencyHz, double gainDb = 0)
    {
        FrequencyHz = frequencyHz;
        GainDb = gainDb;
    }

    public EqBand Clone() => new(FrequencyHz, GainDb);
}
