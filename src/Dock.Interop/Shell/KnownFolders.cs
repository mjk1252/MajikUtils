using System.IO;
using System.Runtime.InteropServices;
using Dock.Interop.Native;

namespace Dock.Interop.Shell;

/// <summary>
/// Resolves the shell folders that <see cref="Environment.SpecialFolder"/> cannot.
///
/// Downloads has no <c>SpecialFolder</c> member at all, and Screenshots is a known folder in its
/// own right rather than a fixed subfolder of Pictures. Both can be relocated to another drive from
/// their Properties, and building either path by hand out of the profile directory is wrong the
/// moment anybody does -- silently, because a watcher pointed at a folder that is not there simply
/// never fires.
/// </summary>
internal static class KnownFolders
{
    private static readonly Guid Downloads = new("374DE290-123F-4565-9164-39C4925E467B");
    private static readonly Guid Screenshots = new("B7BEDE81-DF94-4682-A7D8-57A52620B86F");

    /// <summary>
    /// Where downloads actually go, or null if the shell will not say. Falls back to the profile
    /// only when the call itself fails, which is the one case where a guess beats nothing.
    /// </summary>
    public static string? DownloadsPath() =>
        Resolve(Downloads) ?? Beside(Environment.SpecialFolder.UserProfile, "Downloads");

    /// <summary>
    /// Where Win+PrtScn saves. Falls back to Pictures\Screenshots, which is where it lives until
    /// somebody moves it.
    /// </summary>
    public static string? ScreenshotsPath() =>
        Resolve(Screenshots) ?? Beside(Environment.SpecialFolder.MyPictures, "Screenshots");

    private static string? Beside(Environment.SpecialFolder folder, string name)
    {
        var root = Environment.GetFolderPath(folder);
        return string.IsNullOrEmpty(root) ? null : Path.Combine(root, name);
    }

    private static string? Resolve(Guid id)
    {
        var path = IntPtr.Zero;

        try
        {
            // KF_FLAG_DEFAULT: the current location, redirection included. Deliberately not
            // KF_FLAG_CREATE -- if the folder is not there, that is an answer, not something to fix.
            if (SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out path) != 0)
                return null;

            var resolved = Marshal.PtrToStringUni(path);
            return string.IsNullOrWhiteSpace(resolved) ? null : resolved;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
        finally
        {
            if (path != IntPtr.Zero)
                AudioInterop.CoTaskMemFree(path);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath(
        ref Guid id, uint flags, IntPtr token, out IntPtr path);
}
