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
    private const int MaxPresetNameLength = 60;
    private const int MaxUserPresets = 500;

    // Level meter: peak dBFS is mapped onto 0..1 over this range, and falls at a fixed rate
    // (a "VU strip" ballistic: instant attack, ~1.1 s to fall across the full range).
    private const double MeterFloorDb = -18.0;
    private const double MeterCeilingDb = 6.0; // over-range headroom above full scale
    private const float MeterFallPerTick = 0.03f;

    private readonly AppDataStore _store;
    private readonly EqualizerApoInstallService _apo;
    private readonly StartupRegistrationService _startup;
    private readonly LoopbackMeterService _meter;
    private readonly OutputDeviceService _output;
    private readonly DispatcherTimer _applyDebounce;
    private readonly DispatcherTimer _meterTimer;

    private EqEngine _engine;
    private AppSettings _settings;
    private Preset? _selectedPreset;
    private string _statusMessage = string.Empty;
    private float _levelMeter;
    private volatile float _pendingPeak;
    private bool _apoInstalled;
    private bool _windowActive; // set by MainWindow once it is actually visible
    private bool _saveFailed;
    private bool _disposed;

    public ObservableCollection<BandViewModel> Bands { get; } = new();
    public ObservableCollection<Preset> Presets { get; } = new();

    public MainViewModel()
    {
        _store = new AppDataStore();
        _apo = new EqualizerApoInstallService();
        _startup = new StartupRegistrationService();
        _meter = new LoopbackMeterService();
        _meter.LevelChanged += level => { if (level > _pendingPeak) _pendingPeak = level; };

        _meterTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(33) };
        _meterTimer.Tick += (_, _) => UpdateMeterLevel();

        _output = new OutputDeviceService();
        _output.DeviceChanged += () => Post(() =>
        {
            RaiseSurroundChanged();
            OnPropertyChanged(nameof(Volume));
            OnPropertyChanged(nameof(HasOutputDevice));
            RestartMeter();
            OnEqChanged();
        });
        _output.VolumeChanged += _ => Post(() => OnPropertyChanged(nameof(Volume)));

        _applyDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _applyDebounce.Tick += (_, _) =>
        {
            _applyDebounce.Stop();
            SaveSettingsNow();
            ApplyToEqualizerApo();
        };

        _settings = _store.LoadSettings();

        foreach (var p in PresetLibrary.BuiltIns) Presets.Add(p);
        foreach (var p in SanitizeUserPresets(_store.LoadUserPresets())) Presets.Add(p);

        _selectedPreset = Presets.FirstOrDefault(p => p.Name == _settings.ActivePresetName) ?? Presets[0];
        // Restore the live state (which may differ from the preset if it was edited but never saved)
        // so the UI shows what was actually playing when the app last closed.
        _engine = _settings.LiveEngine?.Sanitized() ?? _selectedPreset.Engine.Clone();
        RebuildBandViewModels();

        _apoInstalled = _apo.IsInstalled;

        // Keep the launch-at-sign-in entry pointing at this exe (e.g. after an upgrade moved it).
        _settings.StartWithWindows = _startup.IsEnabled();
        if (_settings.StartWithWindows) _startup.SetEnabled(true);

        // Make the audio match what the UI is showing.
        ApplyToEqualizerApo();
        UpdateMeterRunning();
    }

    private static IEnumerable<Preset> SanitizeUserPresets(IEnumerable<Preset> loaded)
    {
        var taken = new HashSet<string>(PresetLibrary.BuiltIns.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var p in loaded.Take(MaxUserPresets))
        {
            if (p is null || string.IsNullOrWhiteSpace(p.Name) || p.Engine is null) continue;

            var name = p.Name.Trim();
            if (name.Length > MaxPresetNameLength) name = name[..MaxPresetNameLength];
            // A saved name that collides with a built-in (or another saved preset) is kept but renamed,
            // so it can never be shadowed by the built-in when the active preset is looked up by name.
            var unique = name;
            for (int n = 2; !taken.Add(unique); n++) unique = $"{name} (custom{(n > 2 ? " " + n : "")})";

            yield return new Preset
            {
                Name = unique,
                Description = p.Description ?? string.Empty,
                IsBuiltIn = false,
                Engine = p.Engine.Sanitized(),
            };
        }
    }

    private void Post(Action action)
    {
        if (_disposed) return;
        App.Current.Dispatcher.BeginInvoke(() => { if (!_disposed) action(); });
    }

    // --- Equalizer APO install status ---

    public bool IsApoInstalled => _apoInstalled;

    /// <summary>Re-checks for Equalizer APO (e.g. when the window is re-activated after the user installed it).</summary>
    public void RefreshInstallStatus()
    {
        var installed = _apo.IsInstalled;
        if (installed == _apoInstalled) return;

        _apoInstalled = installed;
        OnPropertyChanged(nameof(IsApoInstalled));
        if (installed) ApplyToEqualizerApo();
    }

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

    /// <summary>The live EQ differs from the selected preset's saved values.</summary>
    public bool IsModified => _selectedPreset is not null && !_engine.IsEquivalentTo(_selectedPreset.Engine);

    public bool CanDeleteSelectedPreset => _selectedPreset is { IsBuiltIn: false };

    private void LoadPreset(Preset preset, bool applyImmediately)
    {
        _selectedPreset = preset;
        _engine = preset.Engine.Clone();
        RebuildBandViewModels();

        OnPropertyChanged(nameof(SelectedPreset));
        OnPropertyChanged(nameof(CanDeleteSelectedPreset));
        OnPropertyChanged(nameof(BassDb));
        OnPropertyChanged(nameof(TrebleDb));
        OnPropertyChanged(nameof(PreampDb));
        OnPropertyChanged(nameof(EnableEqualizer));
        OnPropertyChanged(nameof(AutoGainProtection));
        OnPropertyChanged(nameof(LimiterCeilingDb));
        OnPropertyChanged(nameof(EstimatedPeakDb));
        OnPropertyChanged(nameof(PeakState));
        OnPropertyChanged(nameof(IsModified));
        RaiseSurroundChanged();

        _settings.ActivePresetName = preset.Name;
        SaveSettingsNow();

        if (applyImmediately) ApplyToEqualizerApo();
    }

    private void RebuildBandViewModels()
    {
        Bands.Clear();
        foreach (var band in _engine.Bands)
            Bands.Add(new BandViewModel(band, OnEqChanged, () => BoostLimitDb));
    }

    private double BoostLimitDb => SignalStateCalculator.BoostLimitDb(AutoGainProtection, LimiterCeilingDb);

    private void RefreshBandStates()
    {
        foreach (var band in Bands) band.RefreshState();
    }

    // --- Live-editable EQ state ---

    public double BassDb
    {
        get => _engine.BassDb;
        set { _engine.BassDb = Math.Round(Math.Clamp(value, -12, 12), 1); OnPropertyChanged(); OnEqChanged(); }
    }

    public double TrebleDb
    {
        get => _engine.TrebleDb;
        set { _engine.TrebleDb = Math.Round(Math.Clamp(value, -12, 12), 1); OnPropertyChanged(); OnEqChanged(); }
    }

    public double PreampDb
    {
        get => _engine.PreampDb;
        set { _engine.PreampDb = Math.Round(Math.Clamp(value, -24, 12), 1); OnPropertyChanged(); OnEqChanged(); }
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
            OnEqChanged();
            RefreshBandStates();
        }
    }

    public double LimiterCeilingDb
    {
        get => _engine.Limiter.CeilingDb;
        set
        {
            _engine.Limiter.CeilingDb = Math.Round(Math.Clamp(value, -6, 0), 1);
            OnPropertyChanged();
            OnEqChanged();
            RefreshBandStates();
        }
    }

    // --- System volume (default playback device) ---

    /// <summary>Windows master volume for the current output, 0..100.</summary>
    public double Volume
    {
        get => (_output.Volume ?? 0f) * 100.0;
        set { _output.Volume = (float)(value / 100.0); OnPropertyChanged(); }
    }

    /// <summary>False when no playback device is available; the volume slider is disabled then.</summary>
    public bool HasOutputDevice => _output.Volume.HasValue;

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

    /// <summary>
    /// The Amount slider has no effect once an HRTF file is convolved (it only scales the widening
    /// matrix and the crossfeed), so it is disabled then rather than left as a control that does nothing.
    /// </summary>
    public bool SurroundAmountEnabled =>
        SurroundActive && !(ShowHrtfRow && !string.IsNullOrWhiteSpace(_engine.Surround.HrtfFilePath));

    public double SurroundAmount
    {
        get => _engine.Surround.Amount;
        set { _engine.Surround.Amount = Math.Round(Math.Clamp(value, 0, 1), 2); OnPropertyChanged(); OnEqChanged(); }
    }

    public string HrtfFileName =>
        string.IsNullOrWhiteSpace(_engine.Surround.HrtfFilePath)
            ? "Crossfeed (no HRTF file)"
            : Path.GetFileName(_engine.Surround.HrtfFilePath);

    public void SetHrtfFile(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && !SurroundSettings.IsSafeHrtfPath(path))
        {
            StatusMessage = "That file can't be used. Choose a .wav file stored on this PC (not a network path).";
            return;
        }

        _engine.Surround.HrtfFilePath = string.IsNullOrWhiteSpace(path) ? null : path;
        RaiseSurroundChanged();
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
        OnPropertyChanged(nameof(SurroundAmountEnabled));
        OnPropertyChanged(nameof(ShowHrtfRow));
        OnPropertyChanged(nameof(SurroundAmount));
        OnPropertyChanged(nameof(HrtfFileName));
    }

    /// <summary>Worst-case boost (dB) the curve can produce; 0 while the equalizer is bypassed.</summary>
    public double EstimatedPeakDb => _engine.EnableEqualizer ? AutoGainLimiter.EstimatePeakBoostDb(_engine) : 0.0;

    /// <summary>
    /// Drives the Limiter section's PeakIndicator badge, using the same boost-budget math as the
    /// faders. When auto-gain protection is off no trim is ever applied, so the budget is the fixed
    /// unprotected limit rather than the (then inert) ceiling.
    /// </summary>
    public SignalState PeakState => SignalStateCalculator.FromBoost(EstimatedPeakDb, BoostLimitDb);

    // --- Level meter ---

    /// <summary>Output level as a 0..1 fill (peak dBFS mapped over -18..+6 dB), with VU-style fall-off.</summary>
    public float LevelMeter
    {
        get => _levelMeter;
        private set { _levelMeter = value; OnPropertyChanged(); OnPropertyChanged(nameof(LevelText)); }
    }

    /// <summary>Numeric readout for the meter, e.g. "-12.4 dB" (the same reading the bar shows, with fall-off).</summary>
    public string LevelText => _levelMeter <= 0f
        ? "— dB"
        : string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"{_levelMeter * (MeterCeilingDb - MeterFloorDb) + MeterFloorDb:+0.0;-0.0;0.0} dB");

    private void UpdateMeterLevel()
    {
        float peak = _pendingPeak;
        _pendingPeak = 0f;

        float target = 0f;
        if (peak > 0f)
        {
            double db = 20.0 * Math.Log10(peak);
            target = (float)Math.Clamp((db - MeterFloorDb) / (MeterCeilingDb - MeterFloorDb), 0.0, 1.0);
        }

        float next = target >= _levelMeter ? target : Math.Max(target, _levelMeter - MeterFallPerTick);
        if (Math.Abs(next - _levelMeter) > 0.002f || (next == 0f && _levelMeter != 0f)) LevelMeter = next;
    }

    private bool MeterWanted => _settings.ShowLevelMeter && _windowActive && !_disposed;

    private void UpdateMeterRunning()
    {
        if (MeterWanted)
        {
            _meter.Start();
            _meterTimer.Start();
        }
        else
        {
            _meterTimer.Stop();
            _meter.Stop();
            _pendingPeak = 0f;
            if (_levelMeter != 0f) LevelMeter = 0f;
        }
    }

    private void RestartMeter()
    {
        if (!MeterWanted) return;
        _meter.Stop();
        _meter.Start();
    }

    /// <summary>
    /// The window being hidden to the tray or minimized means nobody can see the meter, so the
    /// loopback capture and its timer are stopped instead of running for nothing.
    /// </summary>
    public void SetWindowActive(bool active)
    {
        if (_windowActive == active) return;
        _windowActive = active;
        UpdateMeterRunning();
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set { _statusMessage = value; OnPropertyChanged(); }
    }

    private void OnEqChanged()
    {
        OnPropertyChanged(nameof(EstimatedPeakDb));
        OnPropertyChanged(nameof(PeakState));
        OnPropertyChanged(nameof(IsModified));
        _applyDebounce.Stop();
        _applyDebounce.Start();
    }

    /// <summary>Persists the live EQ state so the next launch restores exactly what was playing.</summary>
    private void SaveSettingsNow()
    {
        _settings.LiveEngine = _engine.Clone();
        _saveFailed = !_store.SaveSettings(_settings);
    }

    private void ApplyToEqualizerApo()
    {
        var saveNote = _saveFailed ? " Settings could not be saved to disk." : string.Empty;

        if (!_apo.IsInstalled)
        {
            StatusMessage = "Equalizer APO not installed — your changes are kept but not applied yet." + saveNote;
            return;
        }

        try
        {
            var rendered = EqualizerApoConfigGenerator.Generate(_engine.ResolveSurround(_output.Kind));
            _apo.ApplyConfig(rendered);
            StatusMessage = $"Applied at {DateTime.Now:HH:mm:ss}." + saveNote;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            StatusMessage = $"Could not write Equalizer APO config: {ex.Message}" + saveNote;
        }
    }

    // --- Preset management ---

    /// <summary>Saves the live EQ as a named user preset. Returns false (with a status message) if the name is unusable.</summary>
    public bool SaveAsNewPreset(string? rawName)
    {
        var name = rawName?.Trim() ?? string.Empty;

        if (name.Length == 0)
        {
            StatusMessage = "Enter a name for the preset first.";
            return false;
        }

        if (name.Length > MaxPresetNameLength)
        {
            StatusMessage = $"Preset names are limited to {MaxPresetNameLength} characters.";
            return false;
        }

        if (PresetLibrary.BuiltIns.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            StatusMessage = $"\"{name}\" is a built-in preset. Choose a different name.";
            return false;
        }

        var existing = Presets.FirstOrDefault(p =>
            !p.IsBuiltIn && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        var newPreset = new Preset { Name = name, Engine = _engine.Clone(), IsBuiltIn = false };

        if (existing is not null) Presets.Remove(existing);
        Presets.Add(newPreset);

        var saved = _store.SaveUserPresets(Presets.Where(p => !p.IsBuiltIn));
        LoadPreset(newPreset, applyImmediately: false);

        StatusMessage = saved
            ? (existing is not null ? $"Updated preset \"{name}\"." : $"Saved preset \"{name}\".")
            : $"Preset \"{name}\" is active but could not be saved to disk.";
        return true;
    }

    public void DeleteSelectedPreset()
    {
        if (_selectedPreset is null || _selectedPreset.IsBuiltIn) return;

        var name = _selectedPreset.Name;
        Presets.Remove(_selectedPreset);
        var saved = _store.SaveUserPresets(Presets.Where(p => !p.IsBuiltIn));
        LoadPreset(Presets[0], applyImmediately: true);

        StatusMessage = (saved ? $"Deleted preset \"{name}\"." : $"Deleted \"{name}\", but the change could not be saved to disk.") +
                        $" Switched to {Presets[0].Name}.";
    }

    /// <summary>Sets every band, Bass, Treble and Preamp back to 0 dB (surround and limiter settings are kept).</summary>
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
        // The registry is the source of truth, so the toggle can't drift from what Windows will really do.
        get => _startup.IsEnabled();
        set
        {
            if (!_startup.SetEnabled(value))
                StatusMessage = "Could not change the Windows startup setting.";
            _settings.StartWithWindows = _startup.IsEnabled();
            SaveSettingsNow();
            OnPropertyChanged();
        }
    }

    public bool ShowLevelMeter
    {
        get => _settings.ShowLevelMeter;
        set
        {
            _settings.ShowLevelMeter = value;
            SaveSettingsNow();
            UpdateMeterRunning();
            OnPropertyChanged();
        }
    }

    /// <summary>True exactly once, the first time the window is closed to the tray, so the hint is shown once.</summary>
    public bool ConsumeTrayHint()
    {
        if (_settings.TrayHintShown) return false;
        _settings.TrayHintShown = true;
        SaveSettingsNow();
        return true;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose()
    {
        if (_disposed) return;

        if (_applyDebounce.IsEnabled)
        {
            _applyDebounce.Stop();
            SaveSettingsNow();
            ApplyToEqualizerApo();
        }

        _disposed = true;
        _meterTimer.Stop();
        _meter.Dispose();
        _output.Dispose();
    }
}
