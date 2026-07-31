namespace Dock.Core.Models;

public sealed class RunningAppGroup
{
    public required string ProcessPath { get; init; }
    public required string DisplayName { get; init; }
    public List<RunningWindow> Windows { get; } = [];
}
