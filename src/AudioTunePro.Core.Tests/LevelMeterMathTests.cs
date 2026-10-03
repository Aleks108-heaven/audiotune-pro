using AudioTunePro.Core.Services;

namespace AudioTunePro.Core.Tests;

public class LevelMeterMathTests
{
    [Theory]
    [InlineData(0f, 0f)]            // silence
    [InlineData(-1f, 0f)]           // invalid
    [InlineData(float.NaN, 0f)]
    [InlineData(float.PositiveInfinity, 0f)]
    [InlineData(0.0001f, 0f)]       // -80 dB is below the -18 dB floor
    [InlineData(4f, 1f)]            // +12 dB clamps to the top
    public void ToFill_handles_silence_invalid_and_out_of_range_input(float peak, float expected) =>
        Assert.Equal(expected, LevelMeterMath.ToFill(peak));

    [Fact]
    public void Full_scale_sits_three_quarters_along_the_strip()
    {
        // -18..+6 dB: 0 dBFS is 18/24 of the way.
        Assert.Equal(0.75f, LevelMeterMath.ToFill(1f), precision: 4);
    }

    [Fact]
    public void Attack_is_instant()
    {
        Assert.Equal(0.9f, LevelMeterMath.Step(0.2f, 0.9f));
    }

    [Fact]
    public void Release_falls_at_a_fixed_rate_and_never_below_the_target()
    {
        Assert.Equal(0.7f, LevelMeterMath.Step(0.73f, 0f), precision: 4);
        Assert.Equal(0.5f, LevelMeterMath.Step(0.51f, 0.5f)); // target closer than one tick
    }

    [Fact]
    public void A_full_strip_empties_in_about_a_second_at_30fps()
    {
        float level = 1f;
        int ticks = 0;
        while (level > 0f && ticks < 1000) { level = LevelMeterMath.Step(level, 0f); ticks++; }

        Assert.InRange(ticks, 30, 36);
    }

    [Theory]
    [InlineData(0f, "— dB")]
    [InlineData(0.75f, "0.0 dB")]
    [InlineData(1f, "+6.0 dB")]
    [InlineData(0.25f, "-12.0 dB")]
    public void FormatText_shows_a_signed_one_decimal_reading(float fill, string expected) =>
        Assert.Equal(expected, LevelMeterMath.FormatText(fill));

    [Fact]
    public void FormatText_is_culture_invariant()
    {
        var old = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.Equal("-12.0 dB", LevelMeterMath.FormatText(0.25f));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = old; }
    }
}
