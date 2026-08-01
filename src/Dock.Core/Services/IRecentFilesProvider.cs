using Dock.Core.Models;

namespace Dock.Core.Services;

public interface IRecentFilesProvider
{
    IReadOnlyList<RecentFile> GetRecentFiles(int max);
}
