using AudioTunePro.Core.Services;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace AudioTunePro.App.Services;

/// <summary>
/// Cheap system-mix level meter for the UI, using WASAPI loopback capture on
/// the default render device. This is purely a visual aid (AudioTune Pro's actual
/// EQ processing happens inside Equalizer APO's own audio graph, not here, so the
/// reading is the mix *before* the EQ) — the capture buffer is large and callbacks
/// are lightweight, so CPU cost is negligible. Start/Stop can block on the audio
/// service; call them from a background thread.
/// </summary>
#pragma warning disable CS0618 // WasapiLoopbackCapture is marked obsolete in favor of a newer builder API; still functional and simplest for a plain system-output meter.
public sealed class LoopbackMeterService : IDisposable
{
    private WasapiLoopbackCapture? _capture;
    private volatile bool _running;

    public event Action<float>? LevelChanged; // linear peak, 1.0 = 0 dBFS (can exceed 1.0)

    public void Start()
    {
        if (_running) return;
        Stop(); // release a capture that ended on its own (device removed) before creating a new one
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            _capture = new WasapiLoopbackCapture(device);
            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += (_, _) => _running = false;
            _capture.StartRecording();
            _running = true;
        }
        catch (Exception ex)
        {
            // No active render device, or loopback unavailable — meter simply stays idle.
            AppLog.Warn("Level meter could not start", ex);
            _running = false;
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded == 0) return;

        float max = 0f;
        // WasapiLoopbackCapture delivers IEEE float samples.
        int samples = e.BytesRecorded / 4;
        for (int i = 0; i < samples; i++)
        {
            float sample = BitConverter.ToSingle(e.Buffer, i * 4);
            float abs = Math.Abs(sample);
            if (abs > max) max = abs;
        }

        LevelChanged?.Invoke(Math.Min(4f, max)); // float mix can exceed 1.0 (over full scale); up to +12 dB
    }

    public void Stop()
    {
        if (_capture is null) return;
        try { _capture.StopRecording(); }
        catch (Exception ex) { AppLog.Info($"Level meter was already stopped ({ex.GetType().Name})."); }
        _capture.DataAvailable -= OnDataAvailable;
        _capture.Dispose();
        _capture = null;
        _running = false;
    }

    public void Dispose() => Stop();
}
