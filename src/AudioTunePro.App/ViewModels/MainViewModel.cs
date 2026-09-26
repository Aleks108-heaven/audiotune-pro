using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using AudioTunePro.App.Services;
using AudioTunePro.Core.Models;
using AudioTunePro.Core.Presets;
using AudioTunePro.Core.Services;

namespace AudioTunePro.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly AppDataStore _store;
    private readonly EqualizerApoInstallService _apo;
    private readonly StartupRegistrationService _startup;
    private readonly LoopbackMeterService _meter;
    private readonly OutputDeviceService _output;
    private readonly DispatcherTimer _applyDebounce;

    private EqEngine _engine = new();
    private AppSettings _settings;
    private Preset? _selectedPreset;
    private string _statusMessage = string.Empty;
    private float _levelMeter;

    public ObservableCollection<BandViewModel> Bands { get; } = new();
    public ObservableCollection<Preset> Presets { get; } = new();

    public MainViewModel()
    {
        _store = new AppDataStore();
        _apo = new EqualizerApoInstallService();
        _startup = new StartupRegistrationService();
        _meter = new LoopbackMeterService();
        _meter.LevelChanged += level =>
            App.Current.Dispatcher.BeginInvoke(() => LevelMeter = level);

        _output = new OutputDeviceService();
        _output.DeviceChanged += () => App.Current.Dispatcher.BeginInvoke(() =>
        {
            RaiseSurroundChanged();
            OnPropertyChanged(nameof(Volume));
            OnEqChanged();
        });
        _output.VolumeChanged += _ => App.Current.Dispatcher.BeginInvoke(() => OnPropertyChanged(nameof(Volume)));

        _applyDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _applyDebounce.Tick += (_, _) =>
        {
            _applyDebounce.Stop();
            ApplyToEqualizerApo();
        };

        _settings = _store.LoadSettings();

        foreach (var p in PresetLibrary.BuiltIns) Presets.Add(p);
        foreach (var p in _store.LoadUserPresets()) Presets.Add(p);

        var active = Presets.FirstOrDefault(p => p.Name == _settings.ActivePresetName) ?? Presets[0];
        LoadPreset(active, applyImmediately: false);

        RefreshInstallStatus();
        if (_settings.ShowLevelMeter) _meter.Start();
    }

    // --- Equalizer APO install status ---

    public bool IsApoInstalled => _apo.IsInstalled;

    public void RefreshInstallStatus() => OnPropertyChanged(nameof(IsApoInstalled));

    // --- Preset selection ---

    public Preset? SelectedPreset
    {
        get => _selectedPreset;
        set
        {
            if (ReferenceEquals(_selectedPreset, value) || value is null) return;
            LoadPreset(value, applyImmediately: true);
        }
    }

    private void LoadPreset(Preset preset, bool applyImmediately)
    {
        _selectedPreset = preset;
        _engine = preset.Engine.Clone();
        RebuildBandViewModels();

        OnPropertyChanged(nameof(SelectedPreset));
        OnPropertyChanged(nameof(BassDb));
        OnPropertyChanged(nameof(TrebleDb));
        OnPropertyChanged(nameof(PreampDb));
        OnPropertyChanged(nameof(EnableEqualizer));
        OnPropertyChanged(nameof(AutoGainProtection));
        OnPropertyChanged(nameof(LimiterCeilingDb));
        OnPropertyChanged(nameof(EstimatedPeakDb));
        OnPropertyChanged(nameof(PeakState));
        RaiseSurroundChanged();

        _settings.ActivePresetName = preset.Name;
        _store.SaveSettings(_settings);

        if (applyImmediately) ApplyToEqualizerApo();
    }

    private void RebuildBandViewModels()
    {
        Bands.Clear();
        foreach (var band in _engine.Bands)
            Bands.Add(new BandViewModel(band, OnEqChanged, () => LimiterCeilingDb));
    }

    // --- Live-editable EQ state ---

    public double BassDb
    {
        get => _engine.BassDb;
        set { _engine.BassDb = Clamp(value); OnPropertyChanged(); OnEqChanged(); }
    }

    public double TrebleDb
    {
        get => _engine.TrebleDb;
        set { _engine.TrebleDb = Clamp(value); OnPropertyChanged(); OnEqChanged(); }
    }

    public double PreampDb
    {
        get => _engine.PreampDb;
        set { _engine.PreampDb = Math.Clamp(value, -24, 12); OnPropertyChanged(); OnEqChanged(); }
    }

    public bool EnableEqualizer
    {
        get => _engine.EnableEqualizer;
        set { _engine.EnableEqualizer = value; OnPropertyChanged(); OnEqChanged(); }
    }

    public bool AutoGainProtection
    {
        get => _engine.Limiter.AutoGainProtection;
        set
        {
            _engine.Limiter.AutoGainProtection = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(EstimatedPeakDb));
            OnEqChanged();
        }
    }

    public double LimiterCeilingDb
    {
        get => _engine.Limiter.CeilingDb;
        set
        {
            _engine.Limiter.CeilingDb = Math.Clamp(value, -6, 0);
            OnPropertyChanged();
            OnEqChanged();
            foreach (var band in Bands) band.RefreshState();
        }
    }

    // --- System volume (default playback device) ---

    /// <summary>Windows master volume for the current output, 0..100.</summary>
    public double Volume
    {
        get => (_output.Volume ?? 0f) * 100.0;
        set { _output.Volume = (float)(value / 100.0); OnPropertyChanged(); }
    }

    // --- 3D surround ---

    public bool SurroundAuto
    {
        get => _engine.Surround.Mode == SurroundMode.Auto;
        set { if (value) SetSurroundMode(SurroundMode.Auto); }
    }

    /// <summary>The mode actually in effect once Auto is resolved against the current output device.</summary>
    public SurroundMode EffectiveSurroundMode =>
        _engine.ResolveSurround(_output.Kind).Surround.Mode;

    public string SurroundStatus => _engine.Surround.Mode switch
    {
        SurroundMode.Auto => $"Auto: {(_output.Kind == OutputKind.Headphones ? "headphones" : "speakers")} detected" +
                             (string.IsNullOrEmpty(_output.DeviceName) ? "" : $" ({_output.DeviceName})"),
        _ => string.Empty,
    };

    public bool SurroundOff
    {
        get => _engine.Surround.Mode == SurroundMode.Off;
        set { if (value) SetSurroundMode(SurroundMode.Off); }
    }

    public bool SurroundSpeakers
    {
        get => _engine.Surround.Mode == SurroundMode.Speakers;
        set { if (value) SetSurroundMode(SurroundMode.Speakers); }
    }

    public bool SurroundHeadphones
    {
        get => _engine.Surround.Mode == SurroundMode.Headphones;
        set { if (value) SetSurroundMode(SurroundMode.Headphones); }
    }

    /// <summary>Shows the HRTF file row whenever headphone processing is in effect (manual or Auto).</summary>
    public bool ShowHrtfRow => EffectiveSurroundMode == SurroundMode.Headphones;

    public bool SurroundActive => _engine.Surround.Mode != SurroundMode.Off;

    public double SurroundAmount
    {
        get => _engine.Surround.Amount;
        set { _engine.Surround.Amount = Math.Clamp(value, 0, 1); OnPropertyChanged(); OnEqChanged(); }
    }

    public string HrtfFileName =>
        string.IsNullOrWhiteSpace(_engine.Surround.HrtfFilePath)
            ? "Crossfeed (no HRTF file)"
            : Path.GetFileName(_engine.Surround.HrtfFilePath);

    public void SetHrtfFile(string? path)
    {
        _engine.Surround.HrtfFilePath = string.IsNullOrWhiteSpace(path) ? null : path;
        OnPropertyChanged(nameof(HrtfFileName));
        OnEqChanged();
    }

    private void SetSurroundMode(SurroundMode mode)
    {
        if (_engine.Surround.Mode == mode) return;
        _engine.Surround.Mode = mode;
        RaiseSurroundChanged();
        OnEqChanged();
    }

    private void RaiseSurroundChanged()
    {
        OnPropertyChanged(nameof(SurroundAuto));
        OnPropertyChanged(nameof(SurroundStatus));
        OnPropertyChanged(nameof(EffectiveSurroundMode));
        OnPropertyChanged(nameof(SurroundOff));
        OnPropertyChanged(nameof(SurroundSpeakers));
        OnPropertyChanged(nameof(SurroundHeadphones));
        OnPropertyChanged(nameof(SurroundActive));
        OnPropertyChanged(nameof(ShowHrtfRow));
        OnPropertyChanged(nameof(SurroundAmount));
        OnPropertyChanged(nameof(HrtfFileName));
    }

    public double EstimatedPeakDb =>AutoGainLimiter.EstimatePeakBoostDb(_engine);

    /// <summary>
    /// Drives the Limiter section's PeakIndicator badge, using the same gain math as the faders.
    /// When auto-gain protection is off, no trim is ever applied, so the configured ceiling is
    /// inert — the only real threshold left is 0 dBFS (true digital clipping), not the ceiling.
    /// </summary>
    public SignalState PeakState =>
        SignalStateCalculator.FromHeadroom((AutoGainProtection ? LimiterCeilingDb : 0.0) - EstimatedPeakDb);

    public float LevelMeter
    {
        get => _levelMeter;
        private set { _levelMeter = value; OnPropertyChanged(); }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set { _statusMessage = value; OnPropertyChanged(); }
    }

    private static double Clamp(double v) => Math.Clamp(v, -12, 12);

    private void OnEqChanged()
    {
        OnPropertyChanged(nameof(EstimatedPeakDb));
        OnPropertyChanged(nameof(PeakState));
        _applyDebounce.Stop();
        _applyDebounce.Start();
    }

    private void ApplyToEqualizerApo()
    {
        if (!_apo.IsInstalled)
        {
            StatusMessage = "Equalizer APO not installed — changes saved but not applied.";
            return;
        }

        try
        {
            var rendered = EqualizerApoConfigGenerator.Generate(_engine.ResolveSurround(_output.Kind));
            _apo.ApplyConfig(rendered);
            StatusMessage = $"Applied at {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = $"Could not write Equalizer APO config: {ex.Message}";
        }
    }

    // --- Preset management ---

    public void SaveAsNewPreset(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;

        var existing = Presets.FirstOrDefault(p => p.Name == name && !p.IsBuiltIn);
        var newPreset = new Preset { Name = name, Engine = _engine.Clone(), IsBuiltIn = false };

        if (existing is not null) Presets.Remove(existing);
        Presets.Add(newPreset);

        _store.SaveUserPresets(Presets.Where(p => !p.IsBuiltIn));
        LoadPreset(newPreset, applyImmediately: false);
    }

    public void DeleteSelectedPreset()
    {
        if (_selectedPreset is null || _selectedPreset.IsBuiltIn) return;
        Presets.Remove(_selectedPreset);
        _store.SaveUserPresets(Presets.Where(p => !p.IsBuiltIn));
        LoadPreset(Presets[0], applyImmediately: true);
    }

    public void ResetCurrentPreset()
    {
        _engine.Reset();
        RebuildBandViewModels();
        OnPropertyChanged(nameof(BassDb));
        OnPropertyChanged(nameof(TrebleDb));
        OnPropertyChanged(nameof(PreampDb));
        OnEqChanged();
    }

    // --- App settings ---

    public bool StartWithWindows
    {
        get => _settings.StartWithWindows;
        set
        {
            _settings.StartWithWindows = value;
            _startup.SetEnabled(value);
            _store.SaveSettings(_settings);
            OnPropertyChanged();
        }
    }

    public bool ShowLevelMeter
    {
        get => _settings.ShowLevelMeter;
        set
        {
            _settings.ShowLevelMeter = value;
            _store.SaveSettings(_settings);
            if (value) _meter.Start(); else _meter.Stop();
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose()
    {
        _applyDebounce.Stop();
        _meter.Dispose();
        _output.Dispose();
    }
}
