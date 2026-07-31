namespace Dock.Core.Models;

public sealed class InstalledApp
{
    public required string Name { get; init; }
    public required string ExecutablePath { get; init; }
}
