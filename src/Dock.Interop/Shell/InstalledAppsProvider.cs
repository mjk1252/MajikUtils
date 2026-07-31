using Dock.Core.Models;
using Dock.Core.Services;

namespace Dock.Interop.Shell;

/// <summary>
/// Enumerates installed apps by scanning the Start Menu's shortcut folders (per-user and
/// all-users). Doesn't resolve .lnk targets -- SHGetFileInfo and ShellExecute both handle a
/// .lnk path directly (icon + launch), so the shortcut path itself is used as-is.
/// </summary>
public sealed class InstalledAppsProvider : IInstalledAppsProvider
{
    public IReadOnlyList<InstalledApp> GetInstalledApps()
    {
        var results = new List<InstalledApp>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in GetStartMenuRoots())
        {
            if (!Directory.Exists(root))
                continue;

            IEnumerable<string> shortcuts;
            try
            {
                shortcuts = Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var path in shortcuts)
            {
                var name = Path.GetFileNameWithoutExtension(path);
                if (string.IsNullOrWhiteSpace(name) || name.Contains("uninstall", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!seenNames.Add(name))
                    continue;

                results.Add(new InstalledApp { Name = name, ExecutablePath = path });
            }
        }

        return results;
    }

    private static IEnumerable<string> GetStartMenuRoots()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.Programs);
    }
}
