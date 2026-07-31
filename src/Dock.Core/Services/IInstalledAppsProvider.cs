using Dock.Core.Models;

namespace Dock.Core.Services;

public interface IInstalledAppsProvider
{
    IReadOnlyList<InstalledApp> GetInstalledApps();
}
