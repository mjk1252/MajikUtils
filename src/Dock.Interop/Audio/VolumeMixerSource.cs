using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Dock.Core.Models;
using Dock.Core.Services;
using Dock.Interop.Native;

namespace Dock.Interop.Audio;

/// <summary>
/// Every application with an audio session on the default output, read and driven the way the
/// shell's own volume mixer flyout does it -- <c>IAudioSessionManager2</c> enumerates the sessions,
/// and each one's <c>ISimpleAudioVolume</c> is the same interface a session control object answers
/// to when asked for it by QueryInterface.
///
/// Polled rather than pushed. Core Audio does offer session-arrival notifications
/// (<c>IAudioSessionNotification</c>), but a session's own volume and mute have no equivalent push
/// -- something else can move them, the shell's mixer chief among them -- so a poll answers both
/// questions with one mechanism instead of two. A second and a bit is often enough to feel live and
/// cheap enough to leave running for the life of the app, the same trade the removable-drive watch
/// makes for the same reason.
/// </summary>
public sealed class VolumeMixerSource : IVolumeMixerSource, IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(1200);

    private readonly object _gate = new();
    private readonly Dictionary<int, CachedSession> _sessions = [];

    private Timer? _timer;
    private bool _disposed;

    public event EventHandler<IReadOnlyList<AudioSessionInfo>>? Changed;

    public bool Start()
    {
        if (_disposed || _timer is not null)
            return _timer is not null;

        _timer = new Timer(_ => Poll(), null, TimeSpan.Zero, PollInterval);
        return true;
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;

        lock (_gate)
        {
            foreach (var session in _sessions.Values)
                ReleaseSession(session);

            _sessions.Clear();
        }
    }

    /// <summary>
    /// Applied to the cached control from the last poll rather than a freshly enumerated one: a
    /// slider being dragged can call this many times a second, and re-walking the whole session
    /// list for each one is exactly the kind of avoidable COM traffic the equalizer's own capture
    /// loop goes out of its way not to generate.
    /// </summary>
    public void SetVolume(int processId, double level)
    {
        lock (_gate)
        {
            if (!_sessions.TryGetValue(processId, out var session))
                return;

            try
            {
                var context = Guid.Empty;
                session.Volume.SetMasterVolume((float)Math.Clamp(level, 0, 1), ref context);
            }
            catch (COMException)
            {
                // The application closed between the last poll and this call.
            }
        }
    }

    public void SetMuted(int processId, bool muted)
    {
        lock (_gate)
        {
            if (!_sessions.TryGetValue(processId, out var session))
                return;

            try
            {
                var context = Guid.Empty;
                session.Volume.SetMute(muted, ref context);
            }
            catch (COMException)
            {
                // Same as above.
            }
        }
    }

    private void Poll()
    {
        try
        {
            Changed?.Invoke(this, Collect());
        }
        catch (COMException)
        {
            // The default endpoint changed or vanished mid-read. The next tick tries again.
        }
    }

    private List<AudioSessionInfo> Collect()
    {
        var results = new List<AudioSessionInfo>();
        var seen = new HashSet<int>();

        var type = Type.GetTypeFromCLSID(AudioInterop.CLSID_MMDeviceEnumerator);
        if (type is null || Activator.CreateInstance(type) is not AudioInterop.IMMDeviceEnumerator enumerator)
            return results;

        try
        {
            if (enumerator.GetDefaultAudioEndpoint(
                    AudioInterop.EDataFlowRender, AudioInterop.ERoleConsole, out var device) != 0)
            {
                return results;
            }

            var managerId = AudioInterop.IID_IAudioSessionManager2;
            if (device.Activate(ref managerId, AudioInterop.CLSCTX_ALL, IntPtr.Zero, out var managerObj) != 0
                || managerObj is not AudioInterop.IAudioSessionManager2 manager)
            {
                return results;
            }

            try
            {
                CollectSessions(manager, results, seen);
            }
            finally
            {
                Release(manager);
            }
        }
        finally
        {
            Release(enumerator);
        }

        lock (_gate)
        {
            // Whatever is left over closed since the last poll -- its control object is no longer
            // good for anything and holding onto it only leaks a COM reference.
            foreach (var goneId in _sessions.Keys.Where(id => !seen.Contains(id)).ToList())
            {
                ReleaseSession(_sessions[goneId]);
                _sessions.Remove(goneId);
            }
        }

        return results;
    }

    private void CollectSessions(
        AudioInterop.IAudioSessionManager2 manager, List<AudioSessionInfo> results, HashSet<int> seen)
    {
        if (manager.GetSessionEnumerator(out var sessionEnumerator) != 0)
            return;

        try
        {
            if (sessionEnumerator.GetCount(out var count) != 0)
                return;

            for (var i = 0; i < count; i++)
            {
                if (sessionEnumerator.GetSession(i, out var control) != 0 || control is null)
                    continue;

                if (TryDescribe(control, seen, out var info))
                    results.Add(info);
                else
                    Release(control);
            }
        }
        finally
        {
            Release(sessionEnumerator);
        }
    }

    /// <summary>
    /// Reads one session and, if it is worth keeping, caches its control object for
    /// <see cref="SetVolume"/>/<see cref="SetMuted"/> and returns a snapshot of it. Returns false
    /// for a session this should not hold onto -- the caller releases the control in that case.
    /// </summary>
    private bool TryDescribe(
        AudioInterop.IAudioSessionControl2 control, HashSet<int> seen, out AudioSessionInfo info)
    {
        info = null!;

        // Expired sessions are on their way out and process id 0 is the system-sounds session --
        // neither has anything for the mixer to show.
        if (control.GetState(out var state) != 0 || state == AudioInterop.AudioSessionStateExpired)
            return false;

        if (control.GetProcessId(out var pid) != 0 || pid == 0 || !seen.Add((int)pid))
            return false;

        if (control is not AudioInterop.ISimpleAudioVolume volume)
            return false;

        if (volume.GetMasterVolume(out var level) != 0)
            level = 1f;

        volume.GetMute(out var muted);

        var (path, name) = Describe((int)pid);

        info = new AudioSessionInfo((int)pid, path, name, level, muted, state == 1);

        lock (_gate)
        {
            if (_sessions.TryGetValue((int)pid, out var previous))
                ReleaseSession(previous);

            _sessions[(int)pid] = new CachedSession(control, volume);
        }

        return true;
    }

    /// <summary>
    /// The executable path (for an icon) and a name a person would recognise, the same two-step
    /// <c>DeviceUsageMonitor</c> uses for the camera indicator: a process only ever knows its own
    /// path, and the friendly name lives in its version resource.
    /// </summary>
    private static (string Path, string Name) Describe(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            var path = process.MainModule?.FileName;

            if (string.IsNullOrEmpty(path))
                return ("", process.ProcessName);

            var description = FileVersionInfo.GetVersionInfo(path).FileDescription;
            var name = !string.IsNullOrWhiteSpace(description)
                ? description.Trim()
                : Path.GetFileNameWithoutExtension(path);

            return (path, name);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
            or System.ComponentModel.Win32Exception or FileNotFoundException or IOException)
        {
            // The process exited between enumeration and this read, or it runs at a higher
            // integrity level than MajikUtils and its module list cannot be read.
            return ("", "Unknown app");
        }
    }

    private static void ReleaseSession(CachedSession session)
    {
        Release(session.Volume);
        Release(session.Control);
    }

    private static void Release(object? comObject)
    {
        if (comObject is not null && Marshal.IsComObject(comObject))
            Marshal.ReleaseComObject(comObject);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Stop();
    }

    private readonly record struct CachedSession(
        AudioInterop.IAudioSessionControl2 Control, AudioInterop.ISimpleAudioVolume Volume);
}
