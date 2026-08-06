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
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint clsCtx, IntPtr activationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        [PreserveSig] int OpenPropertyStore(uint access, out IntPtr store);
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

    [DllImport("ole32.dll")]
    internal static extern void CoTaskMemFree(IntPtr ptr);
}
