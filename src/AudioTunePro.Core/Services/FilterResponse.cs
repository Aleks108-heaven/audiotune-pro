namespace AudioTunePro.Core.Services;

/// <summary>
/// Magnitude response of one <see cref="FilterSpec"/>, as Equalizer APO would apply it. APO designs these filters
/// with the standard "Audio EQ Cookbook" biquads (peaking, low/high shelf in the center-frequency form with Q, and a
/// high-pass whose Q defaults to 1/√2), at the sample rate of the audio device.
/// </summary>
public static class FilterResponse
{
    /// <summary>Sample rate the on-screen response is drawn for; devices at 44.1 kHz differ only near the top of the audible range.</summary>
    public const double ReferenceSampleRate = 48000;

    /// <summary>Lowest value ever reported: a filter that removes everything reads -120 dB instead of minus infinity.</summary>
    public const double FloorDb = -120;

    /// <summary>
    /// Gain in dB of <paramref name="filter"/> at <paramref name="hz"/>. A filter that cannot be designed (a corner at or
    /// above half the sample rate, a non-finite value) is reported as having no effect, 0 dB.
    /// </summary>
    public static double GainDb(FilterSpec filter, double hz, double sampleRate = ReferenceSampleRate) =>
        Biquad.Design(filter, sampleRate).GainDb(hz, sampleRate);

    /// <summary>One designed biquad, coefficients normalized so that a0 = 1.</summary>
    internal readonly struct Biquad
    {
        private readonly double _b0, _b1, _b2, _a1, _a2;

        private Biquad(double b0, double b1, double b2, double a1, double a2)
        {
            _b0 = b0; _b1 = b1; _b2 = b2; _a1 = a1; _a2 = a2;
        }

        private static Biquad Identity => new(1, 0, 0, 0, 0);

        public static Biquad Design(FilterSpec filter, double sampleRate)
        {
            double fc = filter.FcHz;
            double gainDb = filter.GainDb ?? 0;
            double q = filter.Q ?? Math.Sqrt(0.5);
            if (!double.IsFinite(fc) || !double.IsFinite(gainDb) || !double.IsFinite(q) ||
                fc <= 0 || fc >= sampleRate / 2 || q <= 0)
                return Identity;

            double a = Math.Pow(10, gainDb / 40);
            double omega = 2 * Math.PI * fc / sampleRate;
            double sn = Math.Sin(omega), cs = Math.Cos(omega);
            double alpha = sn / (2 * q);
            double beta = 2 * Math.Sqrt(a) * alpha;

            double b0, b1, b2, a0, a1, a2;
            switch (filter.Shape)
            {
                case FilterShape.Peaking:
                    b0 = 1 + alpha * a; b1 = -2 * cs; b2 = 1 - alpha * a;
                    a0 = 1 + alpha / a; a1 = -2 * cs; a2 = 1 - alpha / a;
                    break;
                case FilterShape.LowShelf:
                    b0 = a * ((a + 1) - (a - 1) * cs + beta);
                    b1 = 2 * a * ((a - 1) - (a + 1) * cs);
                    b2 = a * ((a + 1) - (a - 1) * cs - beta);
                    a0 = (a + 1) + (a - 1) * cs + beta;
                    a1 = -2 * ((a - 1) + (a + 1) * cs);
                    a2 = (a + 1) + (a - 1) * cs - beta;
                    break;
                case FilterShape.HighShelf:
                    b0 = a * ((a + 1) + (a - 1) * cs + beta);
                    b1 = -2 * a * ((a - 1) + (a + 1) * cs);
                    b2 = a * ((a + 1) + (a - 1) * cs - beta);
                    a0 = (a + 1) - (a - 1) * cs + beta;
                    a1 = 2 * ((a - 1) - (a + 1) * cs);
                    a2 = (a + 1) - (a - 1) * cs - beta;
                    break;
                case FilterShape.HighPass:
                    b0 = (1 + cs) / 2; b1 = -(1 + cs); b2 = (1 + cs) / 2;
                    a0 = 1 + alpha; a1 = -2 * cs; a2 = 1 - alpha;
                    break;
                default:
                    return Identity;
            }

            return new Biquad(b0 / a0, b1 / a0, b2 / a0, a1 / a0, a2 / a0);
        }

        /// <summary>
        /// |H|² written in terms of φ = sin²(ω/2), which stays accurate at very low frequencies where the usual cos(ω)
        /// form loses all its digits to cancellation (a high-pass is exactly zero at DC).
        /// </summary>
        public double GainDb(double hz, double sampleRate)
        {
            if (!double.IsFinite(hz) || hz <= 0) return 0;

            double s = Math.Sin(Math.PI * hz / sampleRate);
            double phi = s * s;

            double num = Square(_b0 + _b1 + _b2) - 4 * (_b0 * _b1 + 4 * _b0 * _b2 + _b1 * _b2) * phi + 16 * _b0 * _b2 * phi * phi;
            double den = Square(1 + _a1 + _a2) - 4 * (_a1 + 4 * _a2 + _a1 * _a2) * phi + 16 * _a2 * phi * phi;
            if (!(den > 0)) return 0;
            if (!(num > 0)) return FloorDb;

            return Math.Clamp(10 * Math.Log10(num / den), FloorDb, -FloorDb);
        }

        private static double Square(double x) => x * x;
    }
}
