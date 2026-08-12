using System.Runtime.InteropServices;

namespace Dock.Interop.Native;

/// <summary>
/// The Core Audio surface needed to listen to what the speakers are playing.
///
/// Declared by hand rather than pulled in from a library: this is four interfaces and a struct, and
/// the rest of MajikUtils' Win32 work is hand-declared too. The vtable order below is load-bearing --
/// these are raw COM interfaces, so a method out of sequence calls the wrong function pointer.
/// </summary>
internal static class AudioInterop
{
    internal static readonly Guid CLSID_MMDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    internal static readonly Guid IID_IAudioClient = new("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");
    internal static readonly Guid IID_IAudioCaptureClient = new("C8ADBD64-E71E-48A0-A4DE-185C395CD317");

    /// <summary>Playback devices, as opposed to microphones.</summary>
    internal const int EDataFlowRender = 0;

    /// <summary>The device Windows would send this app's own audio to.</summary>
    internal const int ERoleConsole = 0;

    internal const uint CLSCTX_ALL = 23;

    internal const int AUDCLNT_SHAREMODE_SHARED = 0;

    /// <summary>
    /// The flag that makes this work at all: opened on a *render* endpoint, it captures what is
    /// being played rather than what is being recorded.
    /// </summary>
    internal const uint AUDCLNT_STREAMFLAGS_LOOPBACK = 0x00020000;

    /// <summary>The buffer held no real audio -- the device was idle, so treat it as silence.</summary>
    internal const uint AUDCLNT_BUFFERFLAGS_SILENT = 0x2;

    internal const long ReferenceTimesPerSecond = 10_000_000;

    internal const ushort WAVE_FORMAT_PCM = 1;
    internal const ushort WAVE_FORMAT_IEEE_FLOAT = 3;
    internal const ushort WAVE_FORMAT_EXTENSIBLE = 0xFFFE;

    /// <summary>
    /// Byte offset of WAVEFORMATEXTENSIBLE's SubFormat GUID: the 18-byte WAVEFORMATEX header, then
    /// the 2-byte samples union and the 4-byte channel mask.
    /// </summary>
    internal const int SubFormatOffset = 24;

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    internal struct WAVEFORMATEX
    {
        public ushort wFormatTag;
        public ushort nChannels;
        public uint nSamplesPerSec;
        public uint nAvgBytesPerSec;
        public ushort nBlockAlign;
        public ushort wBitsPerSample;
        public ushort cbSize;
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IMMNotificationClient client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IMMNotificationClient client);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint clsCtx, IntPtr activationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        [PreserveSig] int OpenPropertyStore(uint access, out IPropertyStore store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }

    [ComImport]
    [Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioClient
    {
        [PreserveSig] int Initialize(int shareMode, uint streamFlags, long bufferDuration,
            long periodicity, IntPtr format, IntPtr audioSessionGuid);
        [PreserveSig] int GetBufferSize(out uint frames);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out uint frames);
        [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closestMatch);
        [PreserveSig] int GetMixFormat(out IntPtr format);
        [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr handle);
        [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }

    [ComImport]
    [Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioCaptureClient
    {
        [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags,
            out long devicePosition, out long qpcPosition);
        [PreserveSig] int ReleaseBuffer(uint frames);
        [PreserveSig] int GetNextPacketSize(out uint frames);
    }

    internal static readonly Guid IID_IAudioEndpointVolume = new("5CDF2C82-841E-4546-9722-0CF74078229A");

    /// <summary>
    /// The endpoint's own volume and mute -- what the hardware keys and the taskbar slider move,
    /// as opposed to any one application's level.
    ///
    /// Only GetMasterVolumeLevelScalar and GetMute are ever called, but every member before them
    /// still has to be declared: this is a raw vtable, and a missing entry shifts every slot after
    /// it onto the wrong function.
    /// </summary>
    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
        [PreserveSig] int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid eventContext);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid eventContext);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid eventContext);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    /// <summary>
    /// Told whenever the endpoint's volume moves, by whatever moved it -- a hardware key, the
    /// taskbar slider, another application. Implemented on our side and handed to
    /// <see cref="IAudioEndpointVolume.RegisterControlChangeNotify"/>.
    /// </summary>
    [ComImport]
    [Guid("657804FA-D6AD-4496-8A60-352752AF4F89")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioEndpointVolumeCallback
    {
        [PreserveSig] int OnNotify(IntPtr notificationData);
    }

    /// <summary>
    /// Offsets into AUDIO_VOLUME_NOTIFICATION_DATA, which begins with a 16-byte GUID event context,
    /// then the mute flag, then the master level. Walked as a pointer rather than marshalled as a
    /// struct because it ends in a variable-length per-channel array that nothing here reads.
    /// </summary>
    internal const int NotifyMuteOffset = 16;
    internal const int NotifyMasterVolumeOffset = 20;

    /// <summary>
    /// Told when the endpoints themselves change: one appearing, one going away, or the default
    /// moving to a different device. Implemented on our side and handed to
    /// <see cref="IMMDeviceEnumerator.RegisterEndpointNotificationCallback"/>.
    /// </summary>
    [ComImport]
    [Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMNotificationClient
    {
        [PreserveSig] int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, uint newState);
        [PreserveSig] int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
        [PreserveSig] int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string deviceId);

        [PreserveSig] int OnDefaultDeviceChanged(
            int flow, int role, [MarshalAs(UnmanagedType.LPWStr)] string defaultDeviceId);

        [PreserveSig] int OnPropertyValueChanged(
            [MarshalAs(UnmanagedType.LPWStr)] string deviceId, PROPERTYKEY key);
    }

    /// <summary>
    /// The shell's property bag, which is where an endpoint keeps the name a person would
    /// recognise. The device itself only knows its id, which is a GUID pair nobody wants read out.
    /// </summary>
    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PROPERTYKEY key);
        [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
        [PreserveSig] int SetValue(ref PROPERTYKEY key, ref PROPVARIANT value);
        [PreserveSig] int Commit();
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PROPERTYKEY
    {
        public Guid FormatId;
        public int PropertyId;
    }

    /// <summary>
    /// Only ever read as a string here, so the union is declared as far as the pointer and no
    /// further -- but the <em>size</em> still has to be the real one.
    ///
    /// On 64-bit that is 24 bytes: an 8-byte header, then a union 16 wide, because its largest
    /// members (BLOB, and the counted arrays) are a length plus a pointer. Declaring only the two
    /// fields would imply 16, and the property store would then write eight bytes past the end of
    /// the buffer -- which does not fail, it corrupts whatever was next, and surfaces later as an
    /// access violation somewhere unrelated.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    internal struct PROPVARIANT
    {
        [FieldOffset(0)] public ushort VarType;
        [FieldOffset(8)] public IntPtr Pointer;
    }

    /// <summary>VT_LPWSTR -- the only variant type this reads.</summary>
    internal const ushort VtLpwstr = 31;

    /// <summary>PKEY_Device_FriendlyName: "Speakers (Realtek Audio)" and the like.</summary>
    internal static PROPERTYKEY PKEY_Device_FriendlyName = new()
    {
        FormatId = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"),
        PropertyId = 14
    };

    internal const uint StgmRead = 0;

    [DllImport("ole32.dll")]
    internal static extern int PropVariantClear(ref PROPVARIANT value);

    [DllImport("ole32.dll")]
    internal static extern void CoTaskMemFree(IntPtr ptr);

    // --- Per-application sessions (the volume mixer) -------------------------------------------
    //
    // The same endpoint as the equalizer and the master volume, activated for a different service:
    // rather than the mix Windows is sending to the speakers, this is the list of individual
    // programs contributing to it, each with its own volume and mute the way the shell's own mixer
    // flyout shows them.

    internal static readonly Guid IID_IAudioSessionManager2 = new("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");
    internal static readonly Guid IID_IAudioSessionControl2 = new("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D");
    internal static readonly Guid IID_ISimpleAudioVolume = new("87CE5498-68D6-44E5-9215-6DA47EF883D8");

    /// <summary>A session that has ended, whose control object is on its way out.</summary>
    internal const int AudioSessionStateExpired = 2;

    /// <summary>
    /// Only as far as <c>GetSessionEnumerator</c> -- the notification registration past it is never
    /// called, so nothing needs it declared. <c>IAudioSessionManager2</c> extends
    /// <c>IAudioSessionManager</c>, whose two methods come first in the real vtable and have to be
    /// held here even unused, for the same reason as everywhere else in this file: a raw vtable, a
    /// missing entry, the wrong function called.
    /// </summary>
    [ComImport]
    [Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionManager2
    {
        [PreserveSig] int GetAudioSessionControl(IntPtr audioSessionGuid, uint streamFlags,
            [MarshalAs(UnmanagedType.IUnknown)] out object session);
        [PreserveSig] int GetSimpleAudioVolume(IntPtr audioSessionGuid, uint streamFlags,
            [MarshalAs(UnmanagedType.IUnknown)] out object simpleVolume);
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnumerator);
    }

    [ComImport]
    [Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, out IAudioSessionControl2 session);
    }

    /// <summary>
    /// Declared only as far as <c>GetProcessId</c>: everything this needs (whether the session has
    /// expired, and which process owns it) is at or before that slot. <c>IsSystemSoundsSession</c>
    /// sits right after it but is skippable -- the system-sounds session reports process id 0,
    /// which is a simpler and equally reliable filter.
    /// </summary>
    [ComImport]
    [Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionControl2
    {
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName(out IntPtr name);
        [PreserveSig] int SetDisplayName(IntPtr name, IntPtr eventContext);
        [PreserveSig] int GetIconPath(out IntPtr path);
        [PreserveSig] int SetIconPath(IntPtr path, IntPtr eventContext);
        [PreserveSig] int GetGroupingParam(out Guid groupingParam);
        [PreserveSig] int SetGroupingParam(ref Guid groupingParam, IntPtr eventContext);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr notification);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr notification);
        [PreserveSig] int GetSessionIdentifier(out IntPtr identifier);
        [PreserveSig] int GetSessionInstanceIdentifier(out IntPtr identifier);
        [PreserveSig] int GetProcessId(out uint processId);
    }

    /// <summary>
    /// A session control object also answers to this interface -- QueryInterface rather than a
    /// separate lookup, which is what casting an <see cref="IAudioSessionControl2"/> RCW to this
    /// type does under the hood for classic (non-WinRT) COM.
    /// </summary>
    [ComImport]
    [Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ISimpleAudioVolume
    {
        [PreserveSig] int SetMasterVolume(float level, ref Guid eventContext);
        [PreserveSig] int GetMasterVolume(out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}
