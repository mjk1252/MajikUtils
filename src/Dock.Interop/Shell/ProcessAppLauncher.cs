using System.Diagnostics;
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
        };

        Process.Start(info);
    }
}
