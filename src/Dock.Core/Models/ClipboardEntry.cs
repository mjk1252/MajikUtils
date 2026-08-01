namespace Dock.Core.Models;

public sealed class ClipboardEntry
{
    public required string Text { get; init; }
    public required DateTime CapturedAt { get; init; }
}
