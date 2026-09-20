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

    public double EstimatedPeakDb => AutoGainLimiter.EstimatePeakBoostDb(_engine);

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
            var rendered = EqualizerApoConfigGenerator.Generate(_engine);
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
    }
}
