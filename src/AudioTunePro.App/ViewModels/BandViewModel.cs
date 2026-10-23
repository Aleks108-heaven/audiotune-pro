using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using AudioTunePro.Core.Models;

namespace AudioTunePro.App.ViewModels;

/// <summary>Thin, bindable wrapper around one <see cref="EqBand"/>.</summary>
public sealed class BandViewModel : INotifyPropertyChanged
{
    private readonly EqBand _band;
    private readonly int _bandCount;
    private readonly Action _onChanged;
    private readonly Func<double> _boostLimitDbProvider;

    public BandViewModel(EqBand band, int bandCount, Action onChanged, Func<double> boostLimitDbProvider)
    {
        _band = band;
        _bandCount = bandCount;
        _onChanged = onChanged;
        _boostLimitDbProvider = boostLimitDbProvider;
    }

    // Numbers are shown with a "." whatever the Windows region is, like every dB readout in the window
    // (WPF bindings format with en-US unless told otherwise).
    private static readonly CultureInfo Display = CultureInfo.InvariantCulture;

    /// <summary>Center frequency as printed under the fader: 31.5, 160, 1.25k, 12.5k.</summary>
    public string Label => _band.FrequencyHz >= 1000
        ? string.Create(Display, $"{_band.FrequencyHz / 1000:0.##}k")
        : string.Create(Display, $"{_band.FrequencyHz:0.#}");

    /// <summary>Screen-reader name for the fader, e.g. "250 hertz band gain" or "2.5 kilohertz band gain".</summary>
    public string AccessibleName => (_band.FrequencyHz >= 1000
        ? string.Create(Display, $"{_band.FrequencyHz / 1000:0.##} kilohertz")
        : string.Create(Display, $"{_band.FrequencyHz:0.#} hertz")) + " band gain";

    public double GainDb
    {
        get => _band.GainDb;
        set
        {
            var rounded = Math.Round(Math.Clamp(value, -12, 12), 1);
            if (Math.Abs(_band.GainDb - rounded) < 0.0001) return;
            _band.GainDb = rounded;
            OnPropertyChanged();
            OnPropertyChanged(nameof(BandState));
            OnPropertyChanged(nameof(ToolTipText));
            _onChanged();
        }
    }

    /// <summary>
    /// Hover text that names the band and its current gain, which matters most at 31 bands where the printed
    /// readouts are small. Updates while open as the fader moves.
    /// </summary>
    public string ToolTipText
    {
        get
        {
            var freq = _band.FrequencyHz >= 1000
                ? string.Create(Display, $"{_band.FrequencyHz / 1000:0.##} kHz")
                : string.Create(Display, $"{_band.FrequencyHz:0.#} Hz");
            return string.Create(Display, $"{freq}: {GainDb:+0.0;-0.0;0.0} dB. Double-click or press Delete to reset to 0 dB");
        }
    }

    /// <summary>This band's boost relative to the limiter's boost budget — drives the fader's live fill color.</summary>
    public SignalState BandState => SignalStateCalculator.FromBoost(GainDb, _boostLimitDbProvider());

    /// <summary>Call when the limiter budget changes elsewhere (ceiling or protection toggle), since that shifts this band's headroom too.</summary>
    public void RefreshState() => OnPropertyChanged(nameof(BandState));

    // --- Layout, fixed for the life of this view model (a different band count rebuilds every band) ---

    public bool IsCompact => BandDensity.IsCompact(_bandCount);
    public bool IsDense => BandDensity.IsDense(_bandCount);
    public double FaderWidth => BandDensity.FaderWidth(_bandCount);
    public Thickness ColumnMargin => BandDensity.ColumnMargin(_bandCount);
    public GridLength ReadoutRowHeight => BandDensity.ReadoutRowHeight(_bandCount);

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
