using System.Runtime.InteropServices;
using Dock.Core.Services;
using Dock.Interop.Native;

namespace Dock.Interop.Audio;

/// <summary>
/// Announces the default output moving to a different device.
///
/// Windows switches outputs silently. On a machine with a headset, speakers, an HDMI display and a
/// virtual mixer or two, "where is the sound going" is a real question that the taskbar answers
/// only if you go and open it. This is the headphones-just-connected card, for the case that
/// actually happens on a desktop.
/// </summary>
public sealed class AudioDeviceSource : IAudioDeviceSource, IDisposable
{
    private AudioInterop.IMMDeviceEnumerator? _enumerator;
    private Callback? _callback;

    /// <summary>
    /// The last device announced. Windows raises the notification once per role, so a single switch
    /// arrives three times over; without this the island would say the same thing three times.
    /// </summary>
    private string? _lastAnnouncedId;

    public event EventHandler<string>? DefaultOutputChanged;

    public bool Start()
    {
        if (_enumerator is not null)
            return true;

        try
        {
            var type = Type.GetTypeFromCLSID(AudioInterop.CLSID_MMDeviceEnumerator);
            if (type is null || Activator.CreateInstance(type) is not AudioInterop.IMMDeviceEnumerator created)
                return false;

            _callback = new Callback(OnDefaultChanged);

            if (created.RegisterEndpointNotificationCallback(_callback) != 0)
            {
                _callback = null;
                return false;
            }

            _enumerator = created;

            // Remembered but not announced: this is the device already in use, not a change.
            _lastAnnouncedId = CurrentOutputId(created);
            return true;
        }
        catch (COMException)
        {
            return false;
        }
    }

    public void Stop()
    {
        if (_enumerator is null || _callback is null)
            return;

        try
        {
            _enumerator.UnregisterEndpointNotificationCallback(_callback);
        }
        catch (COMException)
        {
            // The service went away before we could let go, which unregisters us anyway.
        }

        _enumerator = null;
        _callback = null;
    }

    /// <summary>
    /// Called from an audio thread whenever any default endpoint changes, for any data flow and any
    /// role. Only the console render device is reported on: capture is not what anybody means by
    /// "where is the sound going", and the communications and multimedia roles almost always follow
    /// the console one a moment later.
    /// </summary>
    private void OnDefaultChanged(int flow, int role, string deviceId)
    {
        if (flow != AudioInterop.EDataFlowRender || role != AudioInterop.ERoleConsole)
            return;

        // Windows reports the default *cleared* as a null id while a device is being removed.
        if (string.IsNullOrEmpty(deviceId) || deviceId == _lastAnnouncedId)
            return;

        _lastAnnouncedId = deviceId;

        var name = FriendlyName(deviceId);
        if (!string.IsNullOrWhiteSpace(name))
            DefaultOutputChanged?.Invoke(this, name);
    }

    private string? CurrentOutputId(AudioInterop.IMMDeviceEnumerator enumerator)
    {
        try
        {
            return enumerator.GetDefaultAudioEndpoint(
                AudioInterop.EDataFlowRender, AudioInterop.ERoleConsole, out var device) == 0
                && device.GetId(out var id) == 0
                    ? id
                    : null;
        }
        catch (COMException)
        {
            return null;
        }
    }

    /// <summary>
    /// The name a person would recognise, out of the endpoint's property store. Deliberately opens
    /// the device fresh rather than reusing anything from the notification: the callback hands over
    /// an id and nothing else.
    /// </summary>
    private string? FriendlyName(string deviceId)
    {
        var value = default(AudioInterop.PROPVARIANT);

        try
        {
            if (_enumerator is null || _enumerator.GetDevice(deviceId, out var device) != 0)
                return null;

            if (device.OpenPropertyStore(AudioInterop.StgmRead, out var store) != 0)
                return null;

            var key = AudioInterop.PKEY_Device_FriendlyName;
            if (store.GetValue(ref key, out value) != 0 || value.VarType != AudioInterop.VtLpwstr)
                return null;

            return Marshal.PtrToStringUni(value.Pointer);
        }
        catch (COMException)
        {
            return null;
        }
        finally
        {
            // The string belongs to the property store until this is called.
            if (value.Pointer != IntPtr.Zero)
                AudioInterop.PropVariantClear(ref value);
        }
    }

    public void Dispose() => Stop();

    /// <summary>
    /// Our end of the endpoint notifications. Held in a field while registered, because Core Audio
    /// keeps a raw pointer to it and would call into freed memory if it were collected.
    ///
    /// Every member has to be implemented even though only one is wanted -- this is a COM interface,
    /// not an event -- and every one of them has to return zero rather than throw: these arrive on
    /// an audio thread, where an unhandled exception takes the process down.
    /// </summary>
    private sealed class Callback(Action<int, int, string> onDefaultChanged)
        : AudioInterop.IMMNotificationClient
    {
        public int OnDeviceStateChanged(string deviceId, uint newState) => 0;

        public int OnDeviceAdded(string deviceId) => 0;

        public int OnDeviceRemoved(string deviceId) => 0;

        public int OnDefaultDeviceChanged(int flow, int role, string defaultDeviceId)
        {
            try
            {
                onDefaultChanged(flow, role, defaultDeviceId);
            }
            catch (Exception)
            {
                // Never let anything cross back into COM from here.
            }

            return 0;
        }

        public int OnPropertyValueChanged(string deviceId, AudioInterop.PROPERTYKEY key) => 0;
    }
}
