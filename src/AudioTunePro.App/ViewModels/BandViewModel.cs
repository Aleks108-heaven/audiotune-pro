using System.ComponentModel;
using System.Runtime.CompilerServices;
using AudioTunePro.Core.Models;

namespace AudioTunePro.App.ViewModels;

/// <summary>Thin, bindable wrapper around one <see cref="EqBand"/>.</summary>
public sealed class BandViewModel : INotifyPropertyChanged
{
    private readonly EqBand _band;
    private readonly Action _onChanged;
    private readonly Func<double> _boostLimitDbProvider;

    public BandViewModel(EqBand band, Action onChanged, Func<double> boostLimitDbProvider)
    {
        _band = band;
        _onChanged = onChanged;
        _boostLimitDbProvider = boostLimitDbProvider;
    }

    public string Label => _band.FrequencyHz >= 1000
        ? $"{_band.FrequencyHz / 1000:0.#}k"
        : $"{_band.FrequencyHz:0}";

    /// <summary>Screen-reader name for the fader, e.g. "250 hertz".</summary>
    public string AccessibleName => $"{Label.Replace("k", " kilo")} hertz band gain";

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
            _onChanged();
        }
    }

    /// <summary>This band's boost relative to the limiter's boost budget — drives the fader's live fill color.</summary>
    public SignalState BandState => SignalStateCalculator.FromBoost(GainDb, _boostLimitDbProvider());

    /// <summary>Call when the limiter budget changes elsewhere (ceiling or protection toggle), since that shifts this band's headroom too.</summary>
    public void RefreshState() => OnPropertyChanged(nameof(BandState));

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
