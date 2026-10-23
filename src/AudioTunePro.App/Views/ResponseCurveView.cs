using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;

namespace AudioTunePro.App.Views;

/// <summary>
/// Draws the EQ's combined response as one line across the fader row. <see cref="Samples"/> are dB values at evenly
/// spaced positions from the left edge to the right edge (see <c>FrequencyResponse.Sample</c>, which spaces them so the
/// line passes through the fader caps), mapped onto <see cref="RangeDb"/> either side of the vertical center. Anything
/// beyond the range runs along the top or bottom edge instead of leaving the panel.
/// Purely a picture: it takes no clicks (the style turns hit testing off), no focus and has no automation peer, since
/// every value it shows is already available from the faders and sliders themselves.
/// </summary>
public sealed class ResponseCurveView : FrameworkElement
{
    public static readonly DependencyProperty SamplesProperty = DependencyProperty.Register(
        nameof(Samples), typeof(IReadOnlyList<double>), typeof(ResponseCurveView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RangeDbProperty = DependencyProperty.Register(
        nameof(RangeDb), typeof(double), typeof(ResponseCurveView),
        new FrameworkPropertyMetadata(12.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(ResponseCurveView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness), typeof(double), typeof(ResponseCurveView),
        new FrameworkPropertyMetadata(1.5, FrameworkPropertyMetadataOptions.AffectsRender));

    public ResponseCurveView()
    {
        ClipToBounds = true;
    }

    public IReadOnlyList<double>? Samples
    {
        get => (IReadOnlyList<double>?)GetValue(SamplesProperty);
        set => SetValue(SamplesProperty, value);
    }

    /// <summary>The dB value that sits at the top edge (and, negated, at the bottom edge).</summary>
    public double RangeDb
    {
        get => (double)GetValue(RangeDbProperty);
        set => SetValue(RangeDbProperty, value);
    }

    public Brush? Stroke
    {
        get => (Brush?)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var samples = Samples;
        var stroke = Stroke;
        double width = ActualWidth, height = ActualHeight, thickness = StrokeThickness, range = RangeDb;
        if (samples is null || samples.Count < 2 || stroke is null ||
            !(width > 0) || !(height > 0) || !(thickness > 0) || !(range > 0))
            return;

        double half = height / 2;
        double inset = thickness / 2; // keep a clamped line fully visible instead of half outside the panel
        int last = samples.Count - 1;

        Point PointAt(int index)
        {
            double db = double.IsFinite(samples[index]) ? samples[index] : 0;
            return new Point(index * width / last, Math.Clamp(half - db / range * half, inset, height - inset));
        }

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(PointAt(0), isFilled: false, isClosed: false);
            for (int i = 1; i <= last; i++)
                context.LineTo(PointAt(i), isStroked: true, isSmoothJoin: true);
        }
        geometry.Freeze();

        drawingContext.DrawGeometry(null,
            new Pen(stroke, thickness) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round },
            geometry);
    }
}
