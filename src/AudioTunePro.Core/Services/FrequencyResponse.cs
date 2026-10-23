using AudioTunePro.Core.Models;

namespace AudioTunePro.Core.Services;

/// <summary>
/// The combined response of everything an <see cref="EqEngine"/> puts in the Equalizer APO config: the graphic EQ
/// curve plus the Bass, Treble, Clarity and Low-cut filters (the same <see cref="EqFilterPlan"/> that is written to the
/// file). Preamp, the limiter's trim and 3D surround are level or stereo effects and are left out.
/// </summary>
public static class FrequencyResponse
{
    /// <summary>Curve samples per band column; an even number so each band center falls exactly on a sample.</summary>
    public const int PointsPerBand = 8;

    /// <summary>
    /// The response curve as dB values at evenly spaced positions across the fader row. Position 0 is half a band
    /// column left of the first band's center and the last position half a column right of the final band's center,
    /// so with <paramref name="pointsPerBand"/> = 8 the center of band <c>i</c> is sample <c>8 i + 4</c>. The result has
    /// <c>bands * pointsPerBand + 1</c> values. A bypassed equalizer is a flat line.
    /// </summary>
    public static double[] Sample(EqEngine engine, int pointsPerBand = PointsPerBand)
    {
        if (pointsPerBand < 2 || pointsPerBand % 2 != 0)
            throw new ArgumentOutOfRangeException(nameof(pointsPerBand), pointsPerBand, "Use an even number of points per band, at least 2.");

        var model = new Model(engine);
        int bands = Math.Max(model.NodeFrequencies.Length, 1);
        var samples = new double[bands * pointsPerBand + 1];
        for (int j = 0; j < samples.Length; j++)
            samples[j] = model.GainDb(FrequencyAt(model.NodeFrequencies, -0.5 + (double)j / pointsPerBand));
        return samples;
    }

    /// <summary>Gain in dB of the whole engine at one frequency.</summary>
    public static double GainDb(EqEngine engine, double hz) => new Model(engine).GainDb(hz);

    /// <summary>
    /// Frequency at a fractional band position: <paramref name="position"/> = <c>i</c> is the center of band <c>i</c>,
    /// and positions in between follow the frequency axis (equal steps in position are equal ratios in frequency, like
    /// the band layouts themselves). Beyond the first and last band the spacing of the two outermost bands continues.
    /// </summary>
    /// <param name="centersHz">Band centers, ascending.</param>
    public static double FrequencyAt(IReadOnlyList<double> centersHz, double position)
    {
        int n = centersHz.Count;
        if (n == 0) return 1000;
        if (n == 1) return centersHz[0] * Math.Pow(2, position);

        if (position <= 0)
            return centersHz[0] * Math.Pow(centersHz[1] / centersHz[0], position);
        if (position >= n - 1)
            return centersHz[n - 1] * Math.Pow(centersHz[n - 1] / centersHz[n - 2], position - (n - 1));

        int i = (int)Math.Floor(position);
        return centersHz[i] * Math.Pow(centersHz[i + 1] / centersHz[i], position - i);
    }

    private sealed class Model
    {
        private readonly double[] _lnHz;
        private readonly double[] _db;
        private readonly FilterResponse.Biquad[] _filters;
        private readonly bool _bypassed;

        public double[] NodeFrequencies { get; }

        public Model(EqEngine engine)
        {
            var nodes = (engine.Bands ?? new List<EqBand>())
                .Where(b => double.IsFinite(b.FrequencyHz) && b.FrequencyHz > 0)
                .OrderBy(b => b.FrequencyHz)
                .ToArray();
            NodeFrequencies = nodes.Select(b => b.FrequencyHz).ToArray();
            _lnHz = NodeFrequencies.Select(f => Math.Log(f)).ToArray();
            _db = nodes.Select(b => double.IsFinite(b.GainDb) ? b.GainDb : 0).ToArray();

            _bypassed = !engine.EnableEqualizer;
            _filters = _bypassed
                ? Array.Empty<FilterResponse.Biquad>()
                : EqFilterPlan.Create(engine)
                    .Select(f => FilterResponse.Biquad.Design(f, FilterResponse.ReferenceSampleRate))
                    .ToArray();
        }

        public double GainDb(double hz)
        {
            if (_bypassed || !double.IsFinite(hz) || hz <= 0) return 0;

            double total = GraphicDb(hz);
            foreach (var filter in _filters)
                total += filter.GainDb(hz, FilterResponse.ReferenceSampleRate);

            return double.IsFinite(total) ? Math.Max(total, FilterResponse.FloorDb) : 0;
        }

        /// <summary>
        /// Equalizer APO's GraphicEQ rule: straight lines between the points, in dB over log-frequency, and the end
        /// points' gains held beyond the first and last point.
        /// </summary>
        private double GraphicDb(double hz)
        {
            int n = _db.Length;
            if (n == 0) return 0;

            double ln = Math.Log(hz);
            if (ln <= _lnHz[0]) return _db[0];
            if (ln >= _lnHz[n - 1]) return _db[n - 1];

            int right = 1;
            while (_lnHz[right] < ln) right++;

            double span = _lnHz[right] - _lnHz[right - 1];
            if (!(span > 0)) return _db[right];
            double t = (ln - _lnHz[right - 1]) / span;
            return _db[right - 1] + t * (_db[right] - _db[right - 1]);
        }
    }
}
