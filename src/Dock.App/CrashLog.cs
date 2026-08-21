using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Dock.Core.Services;

namespace Dock.App;

/// <summary>
/// Writes down what went wrong, because until now nothing did.
///
/// MajikUtils had no unhandled-exception handling of any kind, which meant a single throw anywhere
/// -- the UI thread, a WASAPI callback, the clipboard listener -- ended the process with no dialog,
/// no log and nothing on disk. From the outside that is indistinguishable from the app closing by
/// itself, and there was no way to tell one cause from another after the fact.
///
/// This is deliberately the dumbest possible sink: a text file next to the settings, appended to
/// under a lock, trimmed when it gets long. No logging framework, no severity levels, no rotation
/// policy -- the entire job is that the next silent exit leaves a stack trace behind.
/// </summary>
public static class CrashLog
{
    private static readonly Lock Gate = new();

    /// <summary>
    /// Past this the file is halved, oldest first. Big enough to hold weeks of the occasional
    /// caught exception, small enough that nobody ever has to think about it being there.
    /// </summary>
    private const int MaxBytes = 256 * 1024;

    public static string Path => AppPaths.FilePath("crash.log");

    /// <summary>
    /// Records one exception. <paramref name="origin"/> is where it came from rather than what it
    /// was -- "dispatcher", "background thread", the name of the source that raised it -- because
    /// the exception already says what it was, and which thread it arrived on is the part that is
    /// otherwise unrecoverable from the stack alone.
    ///
    /// Swallows everything. A logger that can take the app down is worse than no logger.
    /// </summary>
    public static void Record(string origin, Exception? exception, bool fatal = false)
    {
        try
        {
            var entry = new StringBuilder()
                .Append("===== ")
                .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"))
                .Append("  [")
                .Append(fatal ? "fatal" : "handled")
                .Append("]  ")
                .AppendLine(origin)
                .AppendLine(exception?.ToString() ?? "(no exception object)")
                .AppendLine()
                .ToString();

            lock (Gate)
            {
                Trim();
                File.AppendAllText(Path, entry, Encoding.UTF8);
            }
        }
        catch
        {
            // Disk full, folder gone, file locked by an editor. Nothing here is worth a crash.
        }
    }

    /// <summary>
    /// Records something that is not an exception but is worth knowing after the fact.
    ///
    /// The log was built for the silent exits and is the only file the app writes that anybody
    /// ever reads back, which makes it the right place for a fault that shows itself as behaviour
    /// rather than as a stack -- a machine you cannot get your hands on can still send the file.
    /// </summary>
    public static void Note(string origin, string detail)
    {
        try
        {
            var entry = new StringBuilder()
                .Append("===== ")
                .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"))
                .Append("  [note]  ")
                .AppendLine(origin)
                .AppendLine(detail)
                .AppendLine()
                .ToString();

            lock (Gate)
            {
                Trim();
                File.AppendAllText(Path, entry, Encoding.UTF8);
            }
        }
        catch
        {
            // Same as above: nothing written down here is worth a crash.
        }
    }

    /// <summary>
    /// Whether an exception is one the app has any business continuing after.
    ///
    /// A binding failure or a null on a media snapshot leaves everything else intact and is exactly
    /// what the dispatcher handler exists to absorb. Running out of memory or corrupting the heap
    /// does not, and swallowing those would trade an honest crash for an app that limps. The list
    /// is short on purpose: anything not on it is assumed survivable.
    /// </summary>
    public static bool IsFatal(Exception? exception) => exception is
        OutOfMemoryException or
        AccessViolationException or
        SEHException or
        BadImageFormatException or
        TypeInitializationException { InnerException: OutOfMemoryException };

    /// <summary>Drops the oldest half once the file passes <see cref="MaxBytes"/>.</summary>
    private static void Trim()
    {
        var file = new FileInfo(Path);
        if (!file.Exists || file.Length <= MaxBytes)
            return;

        var lines = File.ReadAllLines(Path);
        File.WriteAllLines(Path, lines.Skip(lines.Length / 2), Encoding.UTF8);
    }
}
