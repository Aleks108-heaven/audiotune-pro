using System.ComponentModel;
using System.Runtime.CompilerServices;
using AudioTunePro.Core.Models;

namespace AudioTunePro.App.ViewModels;

/// <summary>Thin, bindable wrapper around one <see cref="EqBand"/>.</summary>
public sealed class BandViewModel : INotifyPropertyChanged
{
    private readonly EqBand _band;
    private readonly Action _onChanged;
    private readonly Func<double> _ceilingDbProvider;

    public BandViewModel(EqBand band, Action onChanged, Func<double> ceilingDbProvider)
    {
        _band = band;
        _onChanged = onChanged;
        _ceilingDbProvider = ceilingDbProvider;
    }

    public string Label => _band.FrequencyHz >= 1000
        ? $"{_band.FrequencyHz / 1000:0.#}k"
        : $"{_band.FrequencyHz:0}";

    public double GainDb
    {
        get => _band.GainDb;
        set
        {
            if (Math.Abs(_band.GainDb - value) < 0.0001) return;
            _band.GainDb = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(BandState));
            _onChanged();
        }
    }

    /// <summary>This band's gain risk relative to the limiter ceiling — drives the fader's live fill color.</summary>
    public SignalState BandState => SignalStateCalculator.FromHeadroom(_ceilingDbProvider() - GainDb);

    /// <summary>Call when the limiter ceiling changes elsewhere, since that shifts this band's headroom too.</summary>
    public void RefreshState() => OnPropertyChanged(nameof(BandState));

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
