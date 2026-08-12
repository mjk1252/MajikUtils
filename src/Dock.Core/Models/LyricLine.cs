namespace Dock.Core.Models;

/// <summary>One line of time-synced lyrics: when it starts, and the words for it.</summary>
public sealed record LyricLine(TimeSpan Offset, string Text);
