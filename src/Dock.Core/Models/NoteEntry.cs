namespace Dock.Core.Models;

/// <summary>A single quick note jotted from the island, newest first in storage.</summary>
public sealed record NoteEntry(string Text, DateTimeOffset CreatedAt);
