using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using AudioTunePro.App.Services;
using AudioTunePro.Core.Models;
using AudioTunePro.Core.Presets;
using AudioTunePro.Core.Services;

namespace AudioTunePro.App.ViewModels;

/// <summary>
/// UI state and commands. Threading rule: the UI thread never waits on the disk, the registry or the Windows
/// audio service (any of them can stall for seconds, which Windows reports as "stopped interacting"). Config
/// and settings writes, volume changes, the level meter and device discovery all run on background workers;
/// results come back through <see cref="Post"/>.
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private const int MaxPresetNameLength = 60;
    private const int MaxUserPresets = 500;

    /// <summary>Windows gives a closing session a few seconds; pending writes get this long to finish.</summary>
    private static readonly TimeSpan ShutdownFlushBudget = TimeSpan.FromSeconds(3);

    private enum MeterCommand { Stop, Start, Restart, Shutdown }

    private readonly AppDataStore _store;
    private readonly EqualizerApoInstallService _apo;
    private readonly StartupRegistrationService _startup;
    private readonly LoopbackMeterService _meter;
    private readonly DispatcherTimer _applyDebounce;
    private readonly DispatcherTimer _meterTimer;

    private readonly LatestWinsWorker<EqEngine> _applyWorker;
    private readonly LatestWinsWorker<string> _saveWorker;
    private readonly LatestWinsWorker<float> _volumeWorker;
    private readonly LatestWinsWorker<MeterCommand> _meterWorker;
    private readonly ManualResetEventSlim _outputReady = new(false);
    private volatile OutputDeviceService? _output;

    private EqEngine _engine;
    private AppSettings _settings;
    private Preset? _selectedPreset;
    private string _statusMessage = string.Empty;
    private float _levelMeter;
    private volatile float _pendingPeak;
    private volatile bool _apoInstalled = true; // assumed until the first background check says otherwise
    private volatile bool _applied;             // the first apply has completed
    private volatile ExternalConfigInfo _external = ExternalConfigInfo.None;
    private int _lastAppliedKind = -1;
    private int _checking;
    private bool _windowActive; // set by MainWindow once it is actually visible
    private volatile bool _saveFailed;
    private volatile bool _disposed;
    private bool _hasOutputDevice;
    private double? _volumeCache;

    public ObservableCollection<BandViewModel> Bands { get; } = new();
    public ObservableCollection<Preset> Presets { get; } = new();

    public MainViewModel()
    {
        _store = new AppDataStore();
        _apo = new EqualizerApoInstallService();
        _startup = new StartupRegistrationService();
        _meter = new LoopbackMeterService();
        _meter.LevelChanged += level => { if (level > _pendingPeak) _pendingPeak = level; };

        _applyWorker = new LatestWinsWorker<EqEngine>(RunApply, "ApplyWorker");
        _saveWorker = new LatestWinsWorker<string>(json => _saveFailed = !_store.WriteSettings(json), "SaveWorker");
        _volumeWorker = new LatestWinsWorker<float>(WriteVolume, "VolumeWorker");
        _meterWorker = new LatestWinsWorker<MeterCommand>(RunMeter, "MeterWorker");

        _meterTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(33) };
        _meterTimer.Tick += (_, _) => UpdateMeterLevel();

        _applyDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _applyDebounce.Tick += (_, _) =>
        {
            _applyDebounce.Stop();
            SaveSettingsNow();
            RequestApply();
        };

        _settings = _store.LoadSettings();

        foreach (var p in PresetLibrary.BuiltIns) Presets.Add(p);
        foreach (var p in SanitizeUserPresets(_store.LoadUserPresets())) Presets.Add(p);

        _selectedPreset = Presets.FirstOrDefault(p => p.Name == _settings.ActivePresetName) ?? Presets[0];
        // Restore the live state (which may differ from the preset if it was edited but never saved)
        // so the UI shows what was actually playing when the app last closed.
        _engine = _settings.LiveEngine?.Sanitized() ?? _selectedPreset.Engine.Clone();
        RebuildBandViewModels();

        // Keep the launch-at-sign-in entry pointing at a copy that exists (e.g. after an upgrade moved it).
        _settings.StartWithWindows = _startup.IsEnabled();
        if (_settings.StartWithWindows) _startup.RepairIfStale();

        StartOutputService();

        // Make the audio match what the UI is showing (on the apply worker, after the output device is known).
        RequestApply();
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

    /// <summary>Runs <paramref name="action"/> on the UI thread; a no-op once the view model or the app is gone.</summary>
    private void Post(Action action)
    {
        if (_disposed) return;
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => { if (!_disposed) action(); });
    }

    // --- Output device (speakers/headphones + volume), discovered off the UI thread ---

    private OutputKind CurrentKind => _output?.Kind ?? OutputKind.Speakers;

    private void StartOutputService()
    {
        Task.Run(() =>
        {
            OutputDeviceService? service = null;
            try
            {
                service = new OutputDeviceService();
                var created = service;
                service.DeviceChanged += () => OnOutputDeviceChanged(created);
                service.VolumeChanged += level => Post(() => OnSystemVolumeChanged(level * 100.0));

                if (_disposed)
                {
                    service.Dispose();
                    return;
                }
                _output = service;
            }
            catch (Exception ex)
            {
                AppLog.Error("The output-device service could not start; assuming speakers", ex);
            }
            finally
            {
                _outputReady.Set();
            }

            if (service is not null && ReferenceEquals(_output, service)) OnOutputDeviceChanged(service);
        });
    }

    /// <summary>Runs on a background thread: reads the device (COM), then updates the UI.</summary>
    private void OnOutputDeviceChanged(OutputDeviceService service)
    {
        float? volume = service.Volume;
        var kind = service.Kind;
        Post(() =>
        {
            _hasOutputDevice = volume.HasValue;
            _volumeCache = (volume ?? 0f) * 100.0;
            RaiseSurroundChanged();
            OnPropertyChanged(nameof(Volume));
            OnPropertyChanged(nameof(HasOutputDevice));
            OnPropertyChanged(nameof(AutoGainTrimDb));
            RestartMeter();
            // Only re-render when the resolved surround mode could differ from what was last written.
            if (Volatile.Read(ref _lastAppliedKind) != (int)kind) OnEqChanged();
        });
    }

    private void WriteVolume(float level)
    {
        if (_output is { } output) output.Volume = level;
    }

    // --- Equalizer APO install status ---

    public bool IsApoInstalled => _apoInstalled;

    /// <summary>
    /// Re-checks (in the background) for an Equalizer APO install the user just finished, and for edits to the
    /// rest of config.txt that change how much gain is stacked on ours; re-applies if either changed.
    /// </summary>
    public void RefreshInstallStatus()
    {
        if (_disposed || !_applied || Interlocked.Exchange(ref _checking, 1) == 1) return;

        Task.Run(() =>
        {
            try
            {
                var installed = _apo.IsInstalled;
                var external = installed ? _apo.ScanExternalConfig() : ExternalConfigInfo.None;
                var known = _external;
                if (installed != _apoInstalled ||
                    Math.Abs(external.PreampDb - known.PreampDb) > 0.05 ||
                    external.LoadsPlugins != known.LoadsPlugins)
                    Post(RequestApply);
            }
            catch (Exception ex)
            {
                AppLog.Warn("Checking the Equalizer APO setup failed", ex);
            }
            finally
            {
                Volatile.Write(ref _checking, 0);
            }
        });
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
        OnPropertyChanged(nameof(AutoGainTrimDb));
        OnPropertyChanged(nameof(PeakState));
        OnPropertyChanged(nameof(ExternalConfigNote));
        OnPropertyChanged(nameof(IsModified));
        RaiseSurroundChanged();

        _settings.ActivePresetName = preset.Name;
        SaveSettingsNow();

        if (applyImmediately) RequestApply();
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

    /// <summary>
    /// Windows master volume for the current output, 0..100. Never reads the device on the UI thread: the value
    /// is cached, refreshed by the device-change and volume notifications, and writes go to a background worker.
    /// Windows echoes our own write back (rounded to the driver's step size); ignoring the echo keeps the thumb steady mid-drag.
    /// </summary>
    public double Volume
    {
        get => _volumeCache ?? 0;
        set
        {
            _volumeCache = value;
            _volumeWorker.Submit((float)(value / 100.0));
            OnPropertyChanged();
        }
    }

    /// <summary>Re-syncs the slider from Windows, ignoring the echo of our own writes.</summary>
    private void OnSystemVolumeChanged(double actual)
    {
        if (_volumeCache is double shown && Math.Abs(actual - shown) < 1.0) return;
        _volumeCache = actual;
        OnPropertyChanged(nameof(Volume));
    }

    /// <summary>False when no playback device is available; the volume slider is disabled then.</summary>
    public bool HasOutputDevice => _hasOutputDevice;

    // --- 3D surround ---

    public bool SurroundAuto
    {
        get => _engine.Surround.Mode == SurroundMode.Auto;
        set { if (value) SetSurroundMode(SurroundMode.Auto); }
    }

    /// <summary>The mode actually in effect once Auto is resolved against the current output device.</summary>
    public SurroundMode EffectiveSurroundMode =>
        _engine.ResolveSurround(CurrentKind).Surround.Mode;

    public string SurroundStatus
    {
        get
        {
            if (_engine.Surround.Mode != SurroundMode.Auto) return string.Empty;
            var name = _output?.DeviceName;
            return $"Auto: {(CurrentKind == OutputKind.Headphones ? "headphones" : "speakers")} detected" +
                   (string.IsNullOrEmpty(name) ? "" : $" ({name})");
        }
    }

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
        if (!string.IsNullOrWhiteSpace(path))
        {
            if (!SurroundSettings.IsSafeHrtfPath(path))
            {
                StatusMessage = "That file can't be used. Choose a .wav file stored on this PC (not a network path).";
                return;
            }

            // A small read of the file's header, only when the user picks a file.
            if (!HrtfFileValidator.TryValidate(path, out var reason))
            {
                StatusMessage = $"That HRTF file can't be used: {reason}.";
                return;
            }
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

    /// <summary>
    /// Worst-case boost (dB) the curve can produce, including Preamp lines in the rest of config.txt;
    /// 0 while the equalizer is bypassed.
    /// </summary>
    public double EstimatedPeakDb =>
        _engine.EnableEqualizer ? AutoGainLimiter.EstimatePeakBoostDb(_engine, _external.PreampDb) : 0.0;

    /// <summary>
    /// Level change (dB, ≤ 0) that auto-gain protection is actually applying right now. Shown beside
    /// the estimated peak because a ceiling below 0 dB trims even a flat curve: the badge can read
    /// Safe while the output is quieter, and the user should see why.
    /// </summary>
    public double AutoGainTrimDb =>
        EqualizerApoConfigGenerator.TotalTrimDb(_engine.ResolveSurround(CurrentKind), _external.PreampDb);

    /// <summary>
    /// Drives the Limiter section's PeakIndicator badge, using the same boost-budget math as the
    /// faders. When auto-gain protection is off no trim is ever applied, so the budget is the fixed
    /// unprotected limit rather than the (then inert) ceiling.
    /// </summary>
    public SignalState PeakState => SignalStateCalculator.FromBoost(EstimatedPeakDb, BoostLimitDb);

    /// <summary>
    /// Tells the user about gain the rest of their Equalizer APO config stacks on top of ours (which AudioTune Pro
    /// does not control) and about plugins it loads. Empty when there is nothing to say.
    /// </summary>
    public string ExternalConfigNote
    {
        get
        {
            var external = _external;
            var parts = new List<string>();
            if (Math.Abs(external.PreampDb) >= 0.05)
            {
                parts.Add(string.Create(CultureInfo.InvariantCulture,
                    $"config.txt adds its own Preamp of {external.PreampDb:+0.0;-0.0} dB") +
                    (AutoGainProtection
                        ? " (counted by the limiter)."
                        : ". Auto-gain protection is off, so it is not compensated."));
            }
            if (external.LoadsPlugins) parts.Add("config.txt loads a VST plugin into the audio service.");
            return string.Join(" ", parts);
        }
    }

    // --- Level meter ---

    /// <summary>Peak level of the system mix as a 0..1 fill (dBFS mapped over -18..+6 dB), with VU-style fall-off.</summary>
    public float LevelMeter
    {
        get => _levelMeter;
        private set { _levelMeter = value; OnPropertyChanged(); OnPropertyChanged(nameof(LevelText)); }
    }

    /// <summary>Numeric readout for the meter, e.g. "-12.4 dB" (the same reading the bar shows, with fall-off).</summary>
    public string LevelText => LevelMeterMath.FormatText(_levelMeter);

    private void UpdateMeterLevel()
    {
        float peak = _pendingPeak;
        _pendingPeak = 0f;

        float next = LevelMeterMath.Step(_levelMeter, LevelMeterMath.ToFill(peak));
        if (Math.Abs(next - _levelMeter) > 0.002f || (next == 0f && _levelMeter != 0f)) LevelMeter = next;
    }

    private bool MeterWanted => _settings.ShowLevelMeter && _windowActive && !_disposed;

    private void UpdateMeterRunning()
    {
        if (MeterWanted)
        {
            _meterWorker.Submit(MeterCommand.Start);
            _meterTimer.Start();
        }
        else
        {
            _meterTimer.Stop();
            _meterWorker.Submit(MeterCommand.Stop);
            _pendingPeak = 0f;
            if (_levelMeter != 0f) LevelMeter = 0f;
        }
    }

    private void RestartMeter()
    {
        if (MeterWanted) _meterWorker.Submit(MeterCommand.Restart);
    }

    /// <summary>Runs on the meter worker: starting or stopping WASAPI loopback capture can block on the audio service.</summary>
    private void RunMeter(MeterCommand command)
    {
        switch (command)
        {
            case MeterCommand.Start: _meter.Start(); break;
            case MeterCommand.Stop: _meter.Stop(); break;
            case MeterCommand.Restart: _meter.Stop(); _meter.Start(); break;
            case MeterCommand.Shutdown: _meter.Dispose(); break;
        }
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
        OnPropertyChanged(nameof(AutoGainTrimDb));
        OnPropertyChanged(nameof(PeakState));
        OnPropertyChanged(nameof(ExternalConfigNote));
        OnPropertyChanged(nameof(IsModified));
        _applyDebounce.Stop();
        _applyDebounce.Start();
    }

    /// <summary>Persists the live EQ state so the next launch restores exactly what was playing.</summary>
    private void SaveSettingsNow()
    {
        _settings.LiveEngine = _engine.Clone();
        // Serialize here, where the settings object is owned; only the disk write happens on the worker.
        _saveWorker.Submit(_store.SerializeSettings(_settings));
    }

    /// <summary>Queues a render-and-write of the current EQ state; the newest request wins.</summary>
    private void RequestApply() => _applyWorker.Submit(_engine.Clone());

    /// <summary>Runs on the apply worker: all the file and registry work for one apply.</summary>
    private void RunApply(EqEngine engine)
    {
        _outputReady.Wait(TimeSpan.FromSeconds(3)); // surround Auto needs the device kind; don't wait forever for it
        var kind = CurrentKind;
        var installed = _apo.IsInstalled;
        var external = ExternalConfigInfo.None;
        string status;

        if (!installed)
        {
            status = "Equalizer APO not installed — your changes are kept but not applied yet.";
        }
        else
        {
            try
            {
                var note = DropUnusableHrtf(engine);
                external = _apo.ScanExternalConfig();
                _apo.ApplyConfig(EqualizerApoConfigGenerator.Generate(engine.ResolveSurround(kind), external.PreampDb));
                Volatile.Write(ref _lastAppliedKind, (int)kind);
                status = $"Applied at {DateTime.Now:HH:mm:ss}." + note;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                AppLog.Warn("Could not write the Equalizer APO config", ex);
                status = $"Could not write Equalizer APO config: {ex.Message}";
            }
        }

        Post(() => ApplyCompleted(installed, external, status));
    }

    /// <summary>
    /// Re-checks the HRTF file at apply time (it may have been replaced since it was chosen) and leaves it out of
    /// the config if it is no longer a small, well-formed WAV. Returns a note for the status line, or empty.
    /// </summary>
    private static string DropUnusableHrtf(EqEngine engine)
    {
        var path = engine.Surround.HrtfFilePath;
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        if (HrtfFileValidator.TryValidate(path, out var reason)) return string.Empty;

        engine.Surround.HrtfFilePath = null;
        return $" HRTF file ignored: {reason}.";
    }

    private void ApplyCompleted(bool installed, ExternalConfigInfo external, string status)
    {
        _applied = true;
        if (installed != _apoInstalled)
        {
            _apoInstalled = installed;
            OnPropertyChanged(nameof(IsApoInstalled));
        }

        var changed = !Equals(external, _external);
        _external = external;
        if (changed)
        {
            OnPropertyChanged(nameof(ExternalConfigNote));
            OnPropertyChanged(nameof(EstimatedPeakDb));
            OnPropertyChanged(nameof(AutoGainTrimDb));
            OnPropertyChanged(nameof(PeakState));
        }

        StatusMessage = status + (_saveFailed ? " Settings could not be saved to disk." : string.Empty);
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

    /// <summary>
    /// Flushes any pending edit to disk and to Equalizer APO, then stops the workers. Waits at most
    /// <see cref="ShutdownFlushBudget"/>, so a stalled disk can't hold up Windows logoff.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        if (_applyDebounce.IsEnabled)
        {
            _applyDebounce.Stop();
            SaveSettingsNow();
            RequestApply();
        }

        _disposed = true;
        _meterTimer.Stop();

        var deadline = DateTime.UtcNow + ShutdownFlushBudget;
        TimeSpan Remaining() => deadline > DateTime.UtcNow ? deadline - DateTime.UtcNow : TimeSpan.Zero;
        if (!_saveWorker.Flush(Remaining()) | !_applyWorker.Flush(Remaining()))
            AppLog.Warn("Shutdown: pending writes did not finish within the time budget.");

        _applyWorker.Dispose();
        _saveWorker.Dispose();
        _volumeWorker.Dispose();
        _meterWorker.Submit(MeterCommand.Shutdown);
        _meterWorker.Dispose();

        var output = _output;
        if (output is not null) Task.Run(output.Dispose);
    }
}
