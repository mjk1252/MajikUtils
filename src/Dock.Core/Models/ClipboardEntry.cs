namespace Dock.Core.Models;

/// <summary>What a clipboard entry actually holds.</summary>
public enum ClipboardKind
{
    Text,

    /// <summary>A bitmap, kept as PNG bytes. Screenshots, mostly.</summary>
    Image,

    /// <summary>Paths copied out of Explorer with Ctrl+C.</summary>
    Files
}

/// <summary>
/// One thing that was on the clipboard.
///
/// A closed set of three shapes rather than an interface with three implementations: the history is
/// a flat list drawn as a flat list, and every consumer -- the row template, the command palette,
/// the writer that puts it back -- has to switch on the kind anyway. Making that switch explicit is
/// honest about it, and keeps the type something the tests can construct in one line.
///
/// Built through the factories rather than an object initialiser, because the three shapes each
/// leave two of the four payload fields empty and there is no arrangement of `required` that says
/// so.
/// </summary>
public sealed class ClipboardEntry
{
    private ClipboardEntry(ClipboardKind kind, DateTime capturedAt)
    {
        Kind = kind;
        CapturedAt = capturedAt;
    }

    public ClipboardKind Kind { get; }

    public DateTime CapturedAt { get; }

    /// <summary>
    /// The text for a text entry, and a plain-language stand-in for the other two -- "Image 1920 x
    /// 1080", "3 files". Never null, because the command palette and the "Copied" announcement both
    /// want one line of words for any entry whatever it holds.
    /// </summary>
    public string Text { get; private init; } = string.Empty;

    /// <summary>PNG bytes for <see cref="ClipboardKind.Image"/>, null otherwise.</summary>
    public byte[]? ImagePng { get; private init; }

    /// <summary>Paths for <see cref="ClipboardKind.Files"/>, empty otherwise.</summary>
    public IReadOnlyList<string> Paths { get; private init; } = [];

    /// <summary>
    /// Roughly what this entry costs to keep. Text and paths are rounding errors next to a 4K
    /// screenshot, so only the image is counted; see DockViewModel's image budget.
    /// </summary>
    public long ByteCost => ImagePng?.LongLength ?? 0;

    /// <summary>
    /// What makes two entries the same thing. Putting an entry back on the clipboard re-triggers
    /// the capture that recorded it, so without an identity to compare, selecting the top entry
    /// would push a copy of itself straight back on top of the list it was selected from.
    ///
    /// The image case hashes rather than compares: two screenshots of the same window differ in a
    /// few pixels and megabytes, and comparing whole buffers on every copy to catch the one case
    /// that matters -- the entry that was just clicked -- is work for nothing.
    /// </summary>
    public string Signature { get; private init; } = string.Empty;

    public static ClipboardEntry ForText(string text, DateTime capturedAt) =>
        new(ClipboardKind.Text, capturedAt)
        {
            Text = text,
            Signature = "text:" + text
        };

    public static ClipboardEntry ForImage(byte[] png, int width, int height, DateTime capturedAt) =>
        new(ClipboardKind.Image, capturedAt)
        {
            Text = $"Image {width} x {height}",
            ImagePng = png,
            Signature = $"image:{png.LongLength}:{Hash(png)}"
        };

    public static ClipboardEntry ForFiles(IReadOnlyList<string> paths, DateTime capturedAt) =>
        new(ClipboardKind.Files, capturedAt)
        {
            Text = paths.Count == 1
                ? System.IO.Path.GetFileName(paths[0])
                : $"{paths.Count} files",
            Paths = paths,
            Signature = "files:" + string.Join('|', paths)
        };

    /// <summary>
    /// FNV-1a over the bytes. Not a checksum anybody is trusting -- it decides whether to skip
    /// re-adding an entry the user just clicked, and the cost of a collision is one missing history
    /// row, not a wrong paste.
    /// </summary>
    private static string Hash(byte[] bytes)
    {
        var hash = 2166136261;

        foreach (var b in bytes)
        {
            hash ^= b;
            hash *= 16777619;
        }

        return hash.ToString("x8");
    }
}
