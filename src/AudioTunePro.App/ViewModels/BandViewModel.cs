using System.ComponentModel;
using System.Runtime.CompilerServices;
using AudioTunePro.Core.Models;

namespace AudioTunePro.App.ViewModels;

/// <summary>Thin, bindable wrapper around one <see cref="EqBand"/>.</summary>
public sealed class BandViewModel : INotifyPropertyChanged
{
    private readonly EqBand _band;
    private readonly Action _onChanged;

    public BandViewModel(EqBand band, Action onChanged)
    {
        _band = band;
        _onChanged = onChanged;
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
            _onChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
