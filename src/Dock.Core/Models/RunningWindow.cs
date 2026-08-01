namespace Dock.Core.Models;

public sealed class RunningWindow
{
    public required IntPtr Handle { get; init; }
    public required string Title { get; init; }
    public required int ProcessId { get; init; }
}
