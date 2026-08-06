using System.Runtime.InteropServices;
using Dock.Core.Services;
using Dock.Interop.Native;

namespace Dock.Interop.Audio;

/// <summary>
/// Reads what the speakers are actually playing and turns it into four band levels.
///
/// WASAPI loopback is the mechanism: the same <c>IAudioClient</c> used to record from a microphone,
/// opened on the *playback* endpoint with <c>AUDCLNT_STREAMFLAGS_LOOPBACK</c>, hands back the mix
/// Windows is sending to the speakers. That means it hears everything -- the browser, a game, a
/// notification -- not just the media session the island is showing, which is the right answer for
/// a visualiser: the bars should move to what you can hear.
///
/// Capture runs on its own thread and publishes off it. The work per window is one Hann pass and a
/// 2048-point FFT roughly forty times a second, which is nothing next to leaving four WPF
/// animations running, but it is still work -- so the island only starts this while the bars are
/// actually on screen.
/// </summary>
public sealed class AudioLoopbackSource : IAudioLevelSource, IDisposable
{
    /// <summary>
    /// Window size. 2048 samples is ~43ms at 48kHz, and 23Hz per bin -- fine enough to separate
    /// bass from low mids, short enough that the bars still land on the beat.
    /// </summary>
    private const int FftSize = 2048;

    /// <summary>New samples between windows: the windows overlap, so the bars update ~46 times a second.</summary>
    private const int HopSize = 1024;

    /// <summary>
    /// The four bands, in Hz. Split roughly by octaves rather than evenly, because pitch is
    /// logarithmic -- four equal slices of 0-20kHz would leave three bars showing hiss.
    /// </summary>
    private static readonly (double Low, double High)[] Bands =
    [
        (30, 160),
        (160, 600),
        (600, 2400),
        (2400, 9000)
    ];

    /// <summary>
    /// How fast a band's reference level falls when nothing that loud comes along again. Each band
    /// is scaled against its own recent peak, so a quiet passage still moves the bars rather than
    /// leaving them flat until the chorus -- which is what a fixed scale does.
    /// </summary>
    private const double GainDecay = 0.995;

    /// <summary>
    /// Floor for that reference. Without it the auto-gain would divide near-silence by
    /// near-silence and paint room tone as a full-scale light show.
    /// </summary>
    private const double MinGain = 0.15;

    /// <summary>
    /// Per-band boost applied before scaling. Music carries far less energy at 5kHz than at 100Hz --
    /// roughly a 1/f slope -- so four bands measured raw leave the treble pair permanently flat
    /// while the bass pair does all the moving. These weights answer that, not any one track.
    /// </summary>
    private static readonly double[] BandWeights = [1.0, 1.4, 2.1, 3.0];

    /// <summary>Below this a band is treated as nothing at all, rather than as very quiet something.</summary>
    private const double NoiseFloor = 0.02;

    /// <summary>How often to publish zeroes while the endpoint is idle, so the bars settle.</summary>
    private static readonly TimeSpan SilenceInterval = TimeSpan.FromMilliseconds(60);

    /// <summary>How long to give the capture thread to report whether it could open the device.</summary>
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(2);

    private readonly double[] _window = new double[FftSize];

    /// <summary>
    /// The sliding window, as a ring. Written to once per sample and read out only once per hop --
    /// shuffling a 2048-sample array down by one for each of 48,000 samples a second would cost
    /// more than everything else here put together.
    /// </summary>
    private readonly double[] _ring = new double[FftSize];

    private readonly double[] _real = new double[FftSize];
    private readonly double[] _imaginary = new double[FftSize];
    private readonly double[] _gain = new double[Bands.Length];

    /// <summary>Reused packet staging buffer, so draining the endpoint allocates nothing.</summary>
    private byte[] _packet = [];

    private int _ringWrite;
    private int _filled;
    private int _sinceLastWindow;

    private Thread? _thread;
    private volatile bool _running;
    private ManualResetEventSlim? _started;
    private volatile bool _startedOk;

    public event EventHandler<double[]>? LevelsChanged;

    public int BandCount => Bands.Length;

    public AudioLoopbackSource()
    {
        for (var i = 0; i < FftSize; i++)
            _window[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (FftSize - 1));

        Array.Fill(_gain, MinGain);
    }

    public bool Start()
    {
        if (_running)
            return _startedOk;

        _running = true;
        _started = new ManualResetEventSlim(false);

        // MTA on purpose: this thread owns the Core Audio objects for its whole life, and an STA
        // one would marshal every call back through a message pump that does not exist here.
        _thread = new Thread(CaptureLoop) { IsBackground = true, Name = "MajikUtils audio" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();

        // Waited on so the caller gets a straight answer about whether this machine will give us
        // the audio, rather than silently drawing nothing.
        _started.Wait(StartTimeout);

        if (!_startedOk)
            Stop();

        return _startedOk;
    }

    public void Stop()
    {
        _running = false;

        var thread = _thread;
        _thread = null;

        // Bounded: the loop's own sleeps are in the tens of milliseconds, so a thread that has not
        // come back by now is stuck in a driver call and is not worth blocking the UI over.
        thread?.Join(TimeSpan.FromSeconds(1));

        // Deliberately not disposed: if that Join timed out, the capture thread is still alive and
        // still holds this, and signalling a disposed event would take the process down from a
        // background thread over a set of decorative bars.
        _started = null;
        _startedOk = false;
    }

    private void CaptureLoop()
    {
        AudioInterop.IAudioClient? client = null;
        AudioInterop.IAudioCaptureClient? capture = null;
        var formatPtr = IntPtr.Zero;

        try
        {
            if (!TryOpen(out client, out capture, out formatPtr, out var format, out var isFloat))
            {
                _startedOk = false;
                _started?.Set();
                return;
            }

            _startedOk = true;
            _started?.Set();

            var lastAudio = DateTime.UtcNow;

            while (_running)
            {
                var got = Drain(capture!, format, isFloat);

                if (got)
                {
                    lastAudio = DateTime.UtcNow;
                }
                else
                {
                    // An idle endpoint produces no packets at all rather than packets of zeroes,
                    // so silence has to be published rather than waited for.
                    if (DateTime.UtcNow - lastAudio > SilenceInterval)
                    {
                        lastAudio = DateTime.UtcNow;
                        Reset();
                        LevelsChanged?.Invoke(this, new double[Bands.Length]);
                    }

                    Thread.Sleep(8);
                }
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or NotSupportedException)
        {
            // The endpoint went away mid-stream (unplugged, default device changed, driver reset).
            // The island falls back to its own animation rather than the app failing over audio.
            _startedOk = false;
            _started?.Set();
        }
        finally
        {
            try
            {
                client?.Stop();
            }
            catch (COMException)
            {
                // Already gone; nothing left to stop.
            }

            if (formatPtr != IntPtr.Zero)
                AudioInterop.CoTaskMemFree(formatPtr);

            Release(capture);
            Release(client);
        }
    }

    private static void Release(object? comObject)
    {
        if (comObject is not null && Marshal.IsComObject(comObject))
            Marshal.ReleaseComObject(comObject);
    }

    private static bool TryOpen(
        out AudioInterop.IAudioClient? client,
        out AudioInterop.IAudioCaptureClient? capture,
        out IntPtr formatPtr,
        out AudioInterop.WAVEFORMATEX format,
        out bool isFloat)
    {
        client = null;
        capture = null;
        formatPtr = IntPtr.Zero;
        format = default;
        isFloat = false;

        var type = Type.GetTypeFromCLSID(AudioInterop.CLSID_MMDeviceEnumerator);
        if (type is null || Activator.CreateInstance(type) is not AudioInterop.IMMDeviceEnumerator enumerator)
            return false;

        try
        {
            if (enumerator.GetDefaultAudioEndpoint(
                    AudioInterop.EDataFlowRender, AudioInterop.ERoleConsole, out var device) != 0)
            {
                return false;
            }

            var clientId = AudioInterop.IID_IAudioClient;
            if (device.Activate(ref clientId, AudioInterop.CLSCTX_ALL, IntPtr.Zero, out var clientObject) != 0)
                return false;

            client = (AudioInterop.IAudioClient)clientObject;

            if (client.GetMixFormat(out formatPtr) != 0 || formatPtr == IntPtr.Zero)
                return false;

            format = Marshal.PtrToStructure<AudioInterop.WAVEFORMATEX>(formatPtr);
            isFloat = IsFloat(formatPtr, format);

            // Only 32-bit float and 16-bit PCM are decoded below. The shared-mode mix format is
            // float on every current Windows build, so this is a guard rather than a real branch.
            if (!isFloat && format.wBitsPerSample != 16)
                return false;

            // A one-second buffer against a loop that drains every few milliseconds: overrun would
            // mean dropped audio, and dropped audio here only ever means a stuttering bar.
            if (client.Initialize(
                    AudioInterop.AUDCLNT_SHAREMODE_SHARED,
                    AudioInterop.AUDCLNT_STREAMFLAGS_LOOPBACK,
                    AudioInterop.ReferenceTimesPerSecond,
                    0, formatPtr, IntPtr.Zero) != 0)
            {
                return false;
            }

            var captureId = AudioInterop.IID_IAudioCaptureClient;
            if (client.GetService(ref captureId, out var captureObject) != 0)
                return false;

            capture = (AudioInterop.IAudioCaptureClient)captureObject;

            return client.Start() == 0;
        }
        finally
        {
            Release(enumerator);
        }
    }

    /// <summary>
    /// WAVE_FORMAT_EXTENSIBLE hides the real sample type in a SubFormat GUID whose first field is
    /// the tag it would otherwise have carried.
    /// </summary>
    private static bool IsFloat(IntPtr formatPtr, AudioInterop.WAVEFORMATEX format)
    {
        if (format.wFormatTag == AudioInterop.WAVE_FORMAT_IEEE_FLOAT)
            return true;

        if (format.wFormatTag != AudioInterop.WAVE_FORMAT_EXTENSIBLE ||
            format.cbSize < AudioInterop.SubFormatOffset - 18 + 16)
        {
            return false;
        }

        var subFormat = Marshal.PtrToStructure<Guid>(formatPtr + AudioInterop.SubFormatOffset);
        return subFormat.ToByteArray()[0] == AudioInterop.WAVE_FORMAT_IEEE_FLOAT;
    }

    /// <summary>
    /// Takes every packet waiting on the endpoint. Returns whether there was any -- an idle device
    /// is not an error, it is the answer.
    /// </summary>
    private bool Drain(AudioInterop.IAudioCaptureClient capture, AudioInterop.WAVEFORMATEX format, bool isFloat)
    {
        var any = false;

        while (_running && capture.GetNextPacketSize(out var packetFrames) == 0 && packetFrames > 0)
        {
            if (capture.GetBuffer(out var data, out var frames, out var flags, out _, out _) != 0)
                break;

            any = true;

            if (frames > 0)
            {
                if ((flags & AudioInterop.AUDCLNT_BUFFERFLAGS_SILENT) != 0)
                    AppendSilence((int)frames, format);
                else
                    Append(data, (int)frames, format, isFloat);
            }

            capture.ReleaseBuffer(frames);
        }

        return any;
    }

    /// <summary>
    /// Copies a packet out of the endpoint's buffer and pushes it through as mono samples. Staged
    /// through a byte array and reinterpreted, rather than marshalled a value at a time: this runs
    /// 48,000 times a second per channel, which is the one place in MajikUtils where that matters.
    /// </summary>
    private void Append(IntPtr data, int frames, AudioInterop.WAVEFORMATEX format, bool isFloat)
    {
        var channels = Math.Max(1, (int)format.nChannels);
        var bytes = frames * format.nBlockAlign;

        if (_packet.Length < bytes)
            _packet = new byte[bytes];

        Marshal.Copy(data, _packet, 0, bytes);

        // Downmixed to mono: the bars are one set, and a stereo split would only make them
        // disagree about the same music.
        if (isFloat)
        {
            var samples = MemoryMarshal.Cast<byte, float>(_packet.AsSpan(0, bytes));
            for (var frame = 0; frame < frames; frame++)
            {
                double sum = 0;
                for (var channel = 0; channel < channels; channel++)
                    sum += samples[frame * channels + channel];

                Push(sum / channels, format.nSamplesPerSec);
            }
        }
        else
        {
            var samples = MemoryMarshal.Cast<byte, short>(_packet.AsSpan(0, bytes));
            for (var frame = 0; frame < frames; frame++)
            {
                double sum = 0;
                for (var channel = 0; channel < channels; channel++)
                    sum += samples[frame * channels + channel] / 32768.0;

                Push(sum / channels, format.nSamplesPerSec);
            }
        }
    }

    private void AppendSilence(int frames, AudioInterop.WAVEFORMATEX format)
    {
        for (var i = 0; i < frames; i++)
            Push(0, format.nSamplesPerSec);
    }

    /// <summary>
    /// Feeds one mono sample into the sliding window, publishing a set of levels every hop. The
    /// window slides rather than refills, so consecutive frames overlap and the bars move smoothly
    /// instead of snapping once per buffer.
    /// </summary>
    private void Push(double sample, uint sampleRate)
    {
        _ring[_ringWrite] = sample;
        _ringWrite = (_ringWrite + 1) % FftSize;

        if (_filled < FftSize)
            _filled++;

        if (_filled < FftSize || ++_sinceLastWindow < HopSize)
            return;

        _sinceLastWindow = 0;
        LevelsChanged?.Invoke(this, Analyse(sampleRate));
    }

    private double[] Analyse(uint sampleRate)
    {
        // The write cursor is also the oldest sample, so the ring unrolls from there.
        for (var i = 0; i < FftSize; i++)
        {
            _real[i] = _ring[(_ringWrite + i) % FftSize] * _window[i];
            _imaginary[i] = 0;
        }

        Fft(_real, _imaginary);

        var levels = new double[Bands.Length];
        var binWidth = (double)sampleRate / FftSize;

        for (var band = 0; band < Bands.Length; band++)
        {
            var (low, high) = Bands[band];
            var first = Math.Max(1, (int)(low / binWidth));
            var last = Math.Min(FftSize / 2 - 1, (int)(high / binWidth));
            if (last < first)
                last = first;

            double sum = 0;
            for (var bin = first; bin <= last; bin++)
                sum += Math.Sqrt(_real[bin] * _real[bin] + _imaginary[bin] * _imaginary[bin]);

            var magnitude = sum / (last - first + 1);

            // Compressed before it is scaled: loudness is logarithmic, and a linear bar spends
            // almost all its time near the floor with an occasional spike to the ceiling. The
            // noise floor is checked against the raw magnitude, so the band boost cannot lift a
            // silent room into a visible bar.
            var value = magnitude < NoiseFloor
                ? 0
                : Math.Log10(1 + magnitude * BandWeights[band]);

            _gain[band] = Math.Max(value, Math.Max(MinGain, _gain[band] * GainDecay));
            levels[band] = Math.Clamp(value / _gain[band], 0, 1);
        }

        return levels;
    }

    /// <summary>Drops the accumulated window, so silence does not leave stale audio to analyse.</summary>
    private void Reset()
    {
        Array.Clear(_ring);
        _ringWrite = 0;
        _filled = 0;
        _sinceLastWindow = 0;
    }

    /// <summary>
    /// In-place iterative radix-2 Cooley-Tukey. Small enough to keep here rather than take a
    /// dependency for: <see cref="FftSize"/> is a power of two by construction, which is the only
    /// thing this implementation asks for.
    /// </summary>
    private static void Fft(double[] real, double[] imaginary)
    {
        var n = real.Length;

        // Bit-reversal permutation, so the butterflies below can run bottom-up in place.
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
                j ^= bit;
            j ^= bit;

            if (i < j)
            {
                (real[i], real[j]) = (real[j], real[i]);
                (imaginary[i], imaginary[j]) = (imaginary[j], imaginary[i]);
            }
        }

        for (var length = 2; length <= n; length <<= 1)
        {
            var angle = -2 * Math.PI / length;
            var stepReal = Math.Cos(angle);
            var stepImaginary = Math.Sin(angle);

            for (var start = 0; start < n; start += length)
            {
                double wReal = 1, wImaginary = 0;

                for (var offset = 0; offset < length / 2; offset++)
                {
                    var a = start + offset;
                    var b = a + length / 2;

                    var tReal = real[b] * wReal - imaginary[b] * wImaginary;
                    var tImaginary = real[b] * wImaginary + imaginary[b] * wReal;

                    real[b] = real[a] - tReal;
                    imaginary[b] = imaginary[a] - tImaginary;
                    real[a] += tReal;
                    imaginary[a] += tImaginary;

                    var nextReal = wReal * stepReal - wImaginary * stepImaginary;
                    wImaginary = wReal * stepImaginary + wImaginary * stepReal;
                    wReal = nextReal;
                }
            }
        }
    }

    public void Dispose() => Stop();
}
