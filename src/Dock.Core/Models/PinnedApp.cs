namespace Dock.Core.Models;

public sealed class PinnedApp
{
    public required string Id { get; init; }
    public required string Name { get; set; }
    public required string ExecutablePath { get; set; }
    public string? Arguments { get; set; }
}
