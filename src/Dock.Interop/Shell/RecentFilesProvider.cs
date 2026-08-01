using Dock.Core.Models;
using Dock.Core.Services;

namespace Dock.Interop.Shell;

/// <summary>
/// Reads Windows' own "Recent Items" folder -- the .lnk shortcuts Explorer (and most apps,
/// via SHAddToRecentDocs) automatically creates whenever a file is opened. No extra tracking
/// needed; this is the same data source Start menu "Recent" lists use.
/// </summary>
public sealed class RecentFilesProvider : IRecentFilesProvider
{
    public IReadOnlyList<RecentFile> GetRecentFiles(int max)
    {
        var recentDir = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
        if (!Directory.Exists(recentDir))
            return [];

        IEnumerable<string> shortcuts;
        try
        {
            shortcuts = Directory.EnumerateFiles(recentDir, "*.lnk", SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        return shortcuts
            .Select(path => new FileInfo(path))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Take(max)
            .Select(f => new RecentFile
            {
                Name = System.IO.Path.GetFileNameWithoutExtension(f.Name),
                Path = f.FullName
            })
            .ToList();
    }
}
