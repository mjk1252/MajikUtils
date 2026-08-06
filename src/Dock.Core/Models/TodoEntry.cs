namespace Dock.Core.Models;

/// <summary>
/// A single task on the island's todo list. Unlike <see cref="NoteEntry"/> this one is not
/// write-once: ticking it off is the whole point, so <see cref="Done"/> is settable.
/// </summary>
public sealed record TodoEntry(string Text, DateTimeOffset CreatedAt)
{
    public bool Done { get; set; }
}
