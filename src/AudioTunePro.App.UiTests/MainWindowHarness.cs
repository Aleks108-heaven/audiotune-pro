using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AudioTunePro.App.Views;

namespace AudioTunePro.App.UiTests;

/// <summary>
/// Loads the real <c>MainWindow.xaml</c> (the file from the app project, not a copy), binds a
/// <see cref="FakeMainViewModel"/> to it and lays it out at a given size, without ever showing a window.
/// <para>
/// <c>MainWindow</c> itself cannot be constructed here: its constructor creates the real view model, which starts the
/// services that write Equalizer APO's config, the registry entry and the Windows volume. So the XAML is parsed as text
/// with two changes that only touch the code-behind link: the <c>x:Class</c> and <c>Icon</c> attributes are dropped and every
/// attribute that names a method of <c>MainWindow</c> (the event handlers) is removed. Everything else, every style,
/// template, binding and trigger, is exactly what the app ships.
/// </para>
/// </summary>
public static class MainWindowHarness
{
    public const double DefaultWidth = 1100, DefaultHeight = 860;
    public const double MinimumWidth = 1040, MinimumHeight = 600;

    /// <summary>Where snapshots go; open them to see what a failing test saw.</summary>
    public static string SnapshotDirectory => Path.Combine(AppContext.BaseDirectory, "UiSnapshots");

    private static readonly Lazy<string> Xaml = new(BuildXaml);

    /// <summary>The window's XAML as text, as read from disk (before the changes described on the class).</summary>
    public static string SourceXaml => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "MainWindow.xaml"));

    /// <summary>Names of the methods in MainWindow's code-behind: what the XAML's event attributes refer to.</summary>
    public static IReadOnlySet<string> CodeBehindMethodNames { get; } =
        typeof(MainWindow)
            .GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .ToHashSet();

    private static string BuildXaml()
    {
        string xaml = SourceXaml;
        xaml = Regex.Replace(xaml, @"\sx:Class=""[^""]*""", "");
        xaml = Regex.Replace(xaml, @"\sIcon=""[^""]*""", "");
        xaml = Regex.Replace(xaml, @"\s[\w:.]+=""([A-Za-z_]\w*)""",
            m => CodeBehindMethodNames.Contains(m.Groups[1].Value) ? "" : m.Value);
        // clr-namespace:X without an assembly means "this assembly" in compiled XAML; the parser here needs it spelled out.
        xaml = Regex.Replace(xaml, @"clr-namespace:(AudioTunePro\.App[\w.]*)""", "clr-namespace:$1;assembly=AudioTunePro\"");
        return xaml;
    }

    /// <summary>Parses the window, binds <paramref name="viewModel"/> and lays it out at the given size. Call on the UI thread.</summary>
    public static Window Open(FakeMainViewModel viewModel, double width = DefaultWidth, double height = DefaultHeight)
    {
        var window = (Window)XamlReader.Parse(Xaml.Value);
        window.DataContext = viewModel;
        window.Width = width;
        window.Height = height;
        Layout(window, width, height);
        return window;
    }

    /// <summary>Measures and arranges the window's content at <paramref name="width"/> x <paramref name="height"/> and lets pending work finish.</summary>
    public static void Layout(Window window, double width, double height)
    {
        var root = (FrameworkElement)window.Content;
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        root.UpdateLayout();
    }

    public static FrameworkElement Root(Window window) => (FrameworkElement)window.Content;

    /// <summary>Releases the window; it was never shown, so there is no handle to close.</summary>
    public static void Dispose(Window window)
    {
        try { window.Close(); }
        catch (InvalidOperationException) { }
    }

    // --- Looking inside the rendered window ---

    public static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    public static ResponseCurveView CurveOf(Window window) => Descendants<ResponseCurveView>(Root(window)).Single();

    /// <summary>The fader caps (the slider thumbs), left to right.</summary>
    public static IReadOnlyList<Thumb> Caps(Window window) =>
        Descendants<Thumb>(Root(window)).Where(t => t.Name == "FaderThumb").OrderBy(t => Bounds(t, window).X).ToList();

    public static IReadOnlyList<Slider> Faders(Window window) =>
        Descendants<Slider>(Root(window)).Where(s => s.Style == Application.Current.Resources["EqFaderSlider"])
            .OrderBy(s => Bounds(s, window).X).ToList();

    /// <summary>Text blocks using one of the window's own text styles (<c>fader-value</c>, <c>band-label</c>), left to right.</summary>
    public static IReadOnlyList<TextBlock> TextsWithStyle(Window window, string styleKey) =>
        Descendants<TextBlock>(Root(window)).Where(t => ReferenceEquals(t.Style, window.Resources[styleKey]))
            .OrderBy(t => Bounds(t, window).X).ToList();

    /// <summary>Where <paramref name="element"/> is, in the window's coordinates, including any rotation applied to it.</summary>
    public static Rect Bounds(FrameworkElement element, Window window) =>
        element.TransformToVisual(Root(window)).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));

    public static Point Center(FrameworkElement element, Window window)
    {
        var b = Bounds(element, window);
        return new Point(b.X + b.Width / 2, b.Y + b.Height / 2);
    }

    // --- Pictures ---

    /// <summary>Saves the window as a PNG under <see cref="SnapshotDirectory"/>, on the window's own background.</summary>
    public static void Snapshot(Window window, string name, double width, double height)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width), (int)Math.Ceiling(height), 96, 96, PixelFormats.Pbgra32);

        var background = new DrawingVisual();
        using (var dc = background.RenderOpen())
            dc.DrawRectangle(window.Background, null, new Rect(0, 0, width, height));
        bitmap.Render(background);
        bitmap.Render(Root(window));

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(SnapshotDirectory);
        using var file = File.Create(Path.Combine(SnapshotDirectory, name + ".png"));
        encoder.Save(file);
    }

    /// <summary>What the response curve drew, read back from the drawing its <c>OnRender</c> recorded (no pixels involved).</summary>
    /// <param name="Points">The polyline's points, in the curve element's own coordinates, left to right.</param>
    /// <param name="Bounds">Where the stroked line is, in the same coordinates, pen width included.</param>
    /// <param name="Thickness">The pen's width.</param>
    public sealed record CurveLine(IReadOnlyList<Point> Points, Rect Bounds, double Thickness);

    public static CurveLine ReadCurveLine(ResponseCurveView view)
    {
        var recorded = VisualTreeHelper.GetDrawing(view)
            ?? throw new InvalidOperationException("The response curve has drawn nothing.");
        var line = Flatten(recorded).OfType<GeometryDrawing>().Single();

        // (GetFlattenedPathGeometry would drop this figure: it is a stroke only, not filled.)
        var figure = PathGeometry.CreateFromGeometry(line.Geometry).Figures.Single();
        var points = new List<Point> { figure.StartPoint };
        foreach (var segment in figure.Segments)
        {
            switch (segment)
            {
                case PolyLineSegment poly: points.AddRange(poly.Points); break;
                case LineSegment single: points.Add(single.Point); break;
                default: throw new InvalidOperationException($"Unexpected segment {segment.GetType().Name} in the curve.");
            }
        }

        return new CurveLine(points, line.Geometry.GetRenderBounds(line.Pen), line.Pen.Thickness);
    }

    private static IEnumerable<Drawing> Flatten(Drawing drawing) =>
        drawing is DrawingGroup group ? group.Children.SelectMany(Flatten) : new[] { drawing };

    // --- Binding errors ---

    /// <summary>
    /// Runs <paramref name="action"/> while listening to WPF's data-binding trace, and returns every warning and error
    /// it raised (a property that does not exist on the view model, a converter that failed, and so on).
    /// </summary>
    public static IReadOnlyList<string> CaptureBindingErrors(Action action)
    {
        PresentationTraceSources.Refresh();
        var source = PresentationTraceSources.DataBindingSource;
        var listener = new CollectingListener();
        var previousLevel = source.Switch.Level;
        source.Listeners.Add(listener);
        source.Switch.Level = SourceLevels.Warning;
        try
        {
            action();
            Trace.Flush();
        }
        finally
        {
            source.Switch.Level = previousLevel;
            source.Listeners.Remove(listener);
        }

        return Regex.Matches(listener.Text, @"System\.Windows\.Data (?:Warning|Error): \d+ : [^\r\n]*")
            .Select(m => m.Value).Distinct().ToList();
    }

    private sealed class CollectingListener : TraceListener
    {
        private readonly StringBuilder _text = new();

        public string Text
        {
            get { lock (_text) return _text.ToString(); }
        }

        public override void Write(string? message) { lock (_text) _text.Append(message); }

        public override void WriteLine(string? message) { lock (_text) _text.AppendLine(message); }
    }
}
