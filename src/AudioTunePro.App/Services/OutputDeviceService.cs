using AudioTunePro.Core.Models;
using AudioTunePro.Core.Services;
using NAudio.CoreAudioApi;

namespace AudioTunePro.App.Services;

/// <summary>
/// Watches the default playback device: classifies it as speakers or headphones (so 3D
/// surround can pick its mode automatically) and exposes its master volume. Purely event
/// driven — no polling, so it costs nothing while idle.
/// Every member can block on the Windows audio service, so callers must use it from a background thread,
/// never the UI thread (see MainViewModel).
/// </summary>
public sealed class OutputDeviceService : IDisposable
{
    // PKEY_AudioEndpoint_FormFactor: {1DA5D803-D492-4EDD-8C23-E0C0FFEE7F0E}, pid 0.
    private static readonly PropertyKey FormFactorKey = new(new Guid("1DA5D803-D492-4EDD-8C23-E0C0FFEE7F0E"), 0);

    private static readonly string[] HeadphoneNameHints =
        { "headphone", "headset", "earphone", "earbud", "airpods", "buds", "hands-free" };

    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly MMDeviceNotificationClient _notifications;
    private MMDevice? _device;

    /// <summary>Raised (on a background thread) when the default device, its jack state or its volume changes.</summary>
    public event Action? DeviceChanged;
    public event Action<float>? VolumeChanged;

    public OutputDeviceService()
    {
        Attach();
        _notifications = _enumerator.CreateNotificationClient(useSynchronizationContext: false);
        _notifications.DefaultDeviceChanged += (_, _) => Refresh();
        _notifications.DeviceStateChanged += (_, _) => Refresh();
        // Fires when a jack is plugged/unplugged and the driver flips the endpoint's form factor.
        _notifications.PropertyValueChanged += (_, _) => Refresh();
    }

    public OutputKind Kind { get; private set; } = OutputKind.Speakers;

    public string DeviceName { get; private set; } = string.Empty;

    /// <summary>Master volume 0..1, or null if there is no usable output device.</summary>
    public float? Volume
    {
        get
        {
            try
            {
                lock (_enumerator) return _device?.AudioEndpointVolume.MasterVolumeLevelScalar;
            }
            catch (Exception ex)
            {
                AppLog.Warn("Could not read the system volume", ex);
                return null;
            }
        }
        set
        {
            if (value is null) return;
            try
            {
                lock (_enumerator)
                {
                    if (_device is not null)
                        _device.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(value.Value, 0f, 1f);
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn("Could not set the system volume (device went away?)", ex);
            }
        }
    }

    private void Attach()
    {
        lock (_enumerator)
        {
            if (_device is not null)
            {
                try { _device.AudioEndpointVolume.OnVolumeNotification -= OnVolume; }
                catch (Exception ex) { AppLog.Warn("Could not detach from the previous output device", ex); }
                _device.Dispose();
                _device = null;
            }

            try
            {
                _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                DeviceName = _device.FriendlyName;
                Kind = Classify(_device);
                _device.AudioEndpointVolume.OnVolumeNotification += OnVolume;
            }
            catch (Exception ex)
            {
                // No active render device.
                AppLog.Info($"No usable output device: {ex.GetType().Name}: {ex.Message}");
                DeviceName = string.Empty;
                Kind = OutputKind.Speakers;
            }
        }
    }

    private static OutputKind Classify(MMDevice device)
    {
        // EndpointFormFactor: 1 = Speakers, 3 = Headphones, 5 = Headset, 6 = Handset.
        try
        {
            if (device.Properties.Contains(FormFactorKey) &&
                device.Properties[FormFactorKey].Value is uint formFactor &&
                formFactor is 3 or 5 or 6)
                return OutputKind.Headphones;
        }
        catch (Exception ex)
        {
            AppLog.Warn("Could not read the output form factor; falling back to the device name", ex);
        }

        var name = device.FriendlyName.ToLowerInvariant();
        return HeadphoneNameHints.Any(name.Contains) ? OutputKind.Headphones : OutputKind.Speakers;
    }

    private void OnVolume(AudioVolumeNotificationData data) => VolumeChanged?.Invoke(data.MasterVolume);

    private void Refresh()
    {
        var (oldKind, oldName, oldId) = (Kind, DeviceName, _device?.ID);
        Attach();
        // The endpoint ID matters too: after a driver reload the same-named device gets a new
        // endpoint, and the UI must re-read its volume and re-enable the slider.
        if (Kind != oldKind || DeviceName != oldName || _device?.ID != oldId) DeviceChanged?.Invoke();
    }

    public void Dispose()
    {
        _notifications.Dispose();
        lock (_enumerator) _device?.Dispose();
        _enumerator.Dispose();
    }
}
