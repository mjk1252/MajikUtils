using System.Runtime.InteropServices;
using Dock.Core.Services;
using Dock.Interop.Native;

namespace Dock.Interop.Audio;

/// <summary>
/// Watches the default output endpoint's volume, so the island can show it moving.
///
/// Pushed rather than polled: Core Audio will call us the instant anything changes the level, which
/// is what makes the readout appear on the same keystroke that moved it rather than up to a poll
/// interval later. That callback arrives on an audio thread, so the reading crosses to the UI as an
/// immutable value like every other source here.
///
/// The endpoint belongs to the system and can be swapped out from under us -- headphones unplugged,
/// a device disabled -- so every call is best-effort and a failure costs the readout rather than
/// the application.
/// </summary>
public sealed class VolumeSource : IVolumeSource, IDisposable
{
    private AudioInterop.IAudioEndpointVolume? _endpoint;
    private Callback? _callback;
    private bool _disposed;

    public event EventHandler<VolumeReading>? Changed;

    public bool Start()
    {
        if (_disposed || _endpoint is not null)
            return _endpoint is not null;

        _endpoint = TryOpenEndpoint();
        if (_endpoint is null)
            return false;

        _callback = new Callback(Publish);

        if (_endpoint.RegisterControlChangeNotify(_callback) != 0)
        {
            _endpoint = null;
            _callback = null;
            return false;
        }

        return true;
    }

    public void Stop()
    {
        if (_endpoint is null || _callback is null)
            return;

        try
        {
            _endpoint.UnregisterControlChangeNotify(_callback);
        }
        catch (COMException)
        {
            // The endpoint went away before we could let go of it, which unregisters us anyway.
        }

        _endpoint = null;
        _callback = null;
    }

    /// <summary>Reads the endpoint directly, for the first value before anything has moved.</summary>
    public VolumeReading? Read()
    {
        if (_endpoint is null)
            return null;

        try
        {
            if (_endpoint.GetMasterVolumeLevelScalar(out var level) != 0)
                return null;

            _endpoint.GetMute(out var muted);
            return new VolumeReading(level, muted);
        }
        catch (COMException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads the notification payload rather than the endpoint. The struct carries the new values,
    /// and calling back into the endpoint from inside its own notification is how deadlocks start.
    /// </summary>
    private void Publish(IntPtr data)
    {
        if (data == IntPtr.Zero)
            return;

        var muted = Marshal.ReadInt32(data, AudioInterop.NotifyMuteOffset) != 0;
        var level = ReadFloat(data, AudioInterop.NotifyMasterVolumeOffset);

        Changed?.Invoke(this, new VolumeReading(Math.Clamp(level, 0, 1), muted));
    }

    private static float ReadFloat(IntPtr address, int offset) =>
        BitConverter.Int32BitsToSingle(Marshal.ReadInt32(address, offset));

    private static AudioInterop.IAudioEndpointVolume? TryOpenEndpoint()
    {
        try
        {
            var type = Type.GetTypeFromCLSID(AudioInterop.CLSID_MMDeviceEnumerator);
            if (type is null || Activator.CreateInstance(type) is not AudioInterop.IMMDeviceEnumerator enumerator)
                return null;

            if (enumerator.GetDefaultAudioEndpoint(
                    AudioInterop.EDataFlowRender, AudioInterop.ERoleConsole, out var device) != 0)
            {
                return null;
            }

            var iid = AudioInterop.IID_IAudioEndpointVolume;
            if (device.Activate(ref iid, AudioInterop.CLSCTX_ALL, IntPtr.Zero, out var instance) != 0)
                return null;

            return instance as AudioInterop.IAudioEndpointVolume;
        }
        catch (COMException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Stop();
    }

    /// <summary>
    /// Our end of the notification. Held in a field for as long as it is registered -- Core Audio
    /// keeps only a raw pointer to it, so letting it be collected would leave the endpoint calling
    /// into freed memory.
    /// </summary>
    private sealed class Callback(Action<IntPtr> onNotify) : AudioInterop.IAudioEndpointVolumeCallback
    {
        public int OnNotify(IntPtr notificationData)
        {
            try
            {
                onNotify(notificationData);
            }
            catch (Exception)
            {
                // Never let an exception cross back into COM: this is called from an audio thread,
                // and an unhandled one there takes the process with it.
            }

            return 0;
        }
    }
}
