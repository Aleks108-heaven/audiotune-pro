using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using AudioTunePro.App.ViewModels;
using AudioTunePro.Core.Models;
using AudioTunePro.Core.Presets;
using AudioTunePro.Core.Services;

namespace AudioTunePro.App.UiTests;

/// <summary>
/// Stand-in for <see cref="MainViewModel"/>: the same property names the window binds to, with fixed values and none of
/// the real thing's services (Equalizer APO, the registry, the audio service, the disk). The bands and the response curve
/// are the real ones: real <see cref="BandViewModel"/>s over a real <see cref="EqEngine"/>, and
/// <see cref="FrequencyResponse"/> for the curve. <c>MainWindowBindingTests</c> checks that every property the window binds
/// to also exists on the real view models, so this class cannot hide a typo in the XAML.
/// </summary>
public sealed class FakeMainViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private bool _enableEqualizer;
    private IReadOnlyList<double> _responseCurve = Array.Empty<double>();

    /// <param name="bandCount">10, 15 or 31.</param>
    /// <param name="equalizerEnabled">False shows the bypassed state.</param>
    /// <param name="fadersOnly">True leaves Bass, Treble, Clarity and Low-cut at zero/off, so the response curve is only the faders.</param>
    /// <param name="gains">Optional per-band gains (dB), in band order; otherwise the Rock preset with a few bands pushed up and down.</param>
    public FakeMainViewModel(int bandCount, bool equalizerEnabled = true, bool fadersOnly = false, Func<int, double>? gains = null)
    {
        BandCount = bandCount;
        _enableEqualizer = equalizerEnabled;

        Engine = PresetLibrary.BuiltIns.First(p => p.Name == "Rock").Engine.WithBandCount(bandCount);
        if (gains is not null)
        {
            for (int i = 0; i < Engine.Bands.Count; i++) Engine.Bands[i].GainDb = gains(i);
        }
        else
        {
            // Pushed up and down so the warn / danger colors and dips are in play.
            Engine.Bands.First(b => b.FrequencyHz >= 3000).GainDb = 9.0;
            Engine.Bands.First(b => b.FrequencyHz >= 8000).GainDb = 11.5;
            Engine.Bands.First(b => b.FrequencyHz >= 500).GainDb = -7.5;
        }

        if (fadersOnly)
        {
            Engine.BassDb = 0; Engine.TrebleDb = 0; Engine.Clarity.Amount = 0; Engine.Clarity.LowCutHz = 0;
        }
        else
        {
            Engine.BassDb = 1; Engine.TrebleDb = 1; Engine.Clarity.Amount = 0.6; Engine.Clarity.LowCutHz = 80;
        }
        Engine.EnableEqualizer = equalizerEnabled;

        foreach (var band in Engine.Bands)
            Bands.Add(new BandViewModel(band, bandCount, () => { }, () => 11.7));

        _responseCurve = FrequencyResponse.Sample(Engine);
        BassDb = Engine.BassDb;
        TrebleDb = Engine.TrebleDb;
        ClarityAmount = Engine.Clarity.Amount;
        LowCutHz = Engine.Clarity.LowCutHz > 0 ? Engine.Clarity.LowCutHz : 25;
        LowCutText = Engine.Clarity.LowCutHz > 0 ? $"{Engine.Clarity.LowCutHz:0} Hz" : "Off";
    }

    public EqEngine Engine { get; }
    public int BandCount { get; }

    // --- The graphic EQ ---
    public ObservableCollection<BandViewModel> Bands { get; } = new();
    public string GraphicEqTitle => $"{BandCount}-BAND GRAPHIC EQ";
    public bool Bands10 { get => BandCount == 10; set { } }
    public bool Bands15 { get => BandCount == 15; set { } }
    public bool Bands31 { get => BandCount == 31; set { } }
    public GridLength ReadoutRowHeight => Bands[0].ReadoutRowHeight;

    public bool EnableEqualizer
    {
        get => _enableEqualizer;
        set { _enableEqualizer = value; Raise(); }
    }

    /// <summary>Replace the curve the way MainViewModel does after an edit: with a new array, raising the change.</summary>
    public IReadOnlyList<double> ResponseCurve
    {
        get => _responseCurve;
        set { _responseCurve = value; Raise(); }
    }

    // --- Presets ---
    public ObservableCollection<Preset> Presets { get; } = new(PresetLibrary.BuiltIns);
    public Preset SelectedPreset { get; set; } = PresetLibrary.BuiltIns.First(p => p.Name == "Clear Sound (Laptop Speakers)");
    public bool IsModified { get; set; }
    public bool CanDeleteSelectedPreset { get; set; }

    // --- Sidebar ---
    public double Volume { get; set; } = 62;
    public bool HasOutputDevice { get; set; } = true;
    public double BassDb { get; set; }
    public double TrebleDb { get; set; }
    public double PreampDb { get; set; } = -1.0;
    public bool SurroundOff { get; set; } = true;
    public bool SurroundAuto { get; set; }
    public bool SurroundSpeakers { get; set; }
    public bool SurroundHeadphones { get; set; }
    public string SurroundStatus { get; set; } = "";
    public bool SurroundAmountEnabled { get; set; }
    public double SurroundAmount { get; set; } = 0.5;
    public bool AutoGainProtection { get; set; } = true;
    public double LimiterCeilingDb { get; set; } = -0.3;
    public double EstimatedPeakDb { get; set; } = 6.5;
    public SignalState PeakState { get; set; } = SignalState.Safe;
    public double AutoGainTrimDb { get; set; } = -6.8;
    public string ExternalConfigNote { get; set; } = "";

    // --- Clarity (under the faders) ---
    public double ClarityAmount { get; set; }
    public double LowCutHz { get; set; }
    public string LowCutText { get; set; } = "Off";

    // --- Meter, status, footer ---
    public bool ShowLevelMeter { get; set; } = true;
    public string LevelText { get; set; } = "-9.4 dB";
    public float LevelMeter { get; set; } = 0.45f;
    public string StatusMessage { get; set; } = "Applied at 15:58:02.";
    public bool StartWithWindows { get; set; } = true;
    public bool IsApoInstalled { get; set; } = true;

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
