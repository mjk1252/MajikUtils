using System.Diagnostics;
using System.IO;
using Dock.Core.Services;

namespace Dock.Interop.Shell;

public sealed class ProcessAppLauncher : IAppLauncher
{
    public void Launch(string path, string? arguments = null)
    {
        var info = new ProcessStartInfo
        {
            FileName = path,
            Arguments = arguments ?? string.Empty,
            UseShellExecute = true,

            // Anything started from here would otherwise inherit *our* working directory, and for
            // an installed copy that is the versioned folder Velopack renames out of the way when
            // it applies an update. A directory cannot be renamed while any process is sitting in
            // it, so one long-lived app launched from the island -- an editor, a file manager, a
            // browser -- silently pinned the install open and every later update failed with
            // "one or more running processes prevented it", naming nothing.
            //
            // The launched thing's own folder is also simply the right answer: it is what the
            // shell passes when you double-click a program, so anything that looks for files
            // beside itself keeps working.
            WorkingDirectory = OwnFolder(path)
        };

        Process.Start(info);
    }

    /// <summary>
    /// The folder to start the target in, or empty to let the shell decide.
    ///
    /// Empty for anything that is not a path on disk -- <see cref="IAppLauncher"/> is also handed
    /// URLs and shell locations, and a made-up parent directory for one of those is worse than no
    /// answer at all.
    /// </summary>
    private static string OwnFolder(string path)
    {
        try
        {
            var folder = Path.GetDirectoryName(Path.GetFullPath(path));
            return folder is not null && Directory.Exists(folder) ? folder : string.Empty;
        }
        catch (Exception)
        {
            // GetFullPath throws on anything malformed enough not to be a path at all, which is
            // exactly the case this returns empty for.
            return string.Empty;
        }
    }
}
