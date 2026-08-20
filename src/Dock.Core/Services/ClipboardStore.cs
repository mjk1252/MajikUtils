using System.Text.Json;
using Dock.Core.Models;

namespace Dock.Core.Services;

/// <summary>
/// Persists the pinned clipboard entries, and only those.
///
/// The rest of the history is deliberately not written down. A clipboard sees passwords, licence
/// keys and one-time codes, and a history that quietly kept every one of them on disk between
/// sessions would be a liability sold as a convenience. Pinning is the user saying "this one, keep
/// it" -- which is consent, and the only thing that turns an entry into a file.
///
/// Pinned images are held as base64 inside the same JSON. Ugly, and correct for the size this can
/// reach: pinning is a deliberate act performed a handful of times, so the alternative -- a cache
/// directory with its own lifetime, orphans and cleanup -- is more machinery than the problem.
/// </summary>
public sealed class ClipboardStore
{
    /// <summary>
    /// The most that will be written out, however many are pinned. A pin is a promise to keep
    /// something, not a filing cabinet, and this file is read synchronously at startup.
    /// </summary>
    public const int MaxPinned = 20;

    /// <summary>
    /// The largest image that will be persisted. A pinned 4K screenshot is ~90MB of base64, and
    /// paying that on every launch to keep a picture somebody pinned last week is a bad trade. The
    /// entry stays pinned for the session; it just does not survive a restart.
    /// </summary>
    public const int MaxPinnedImageBytes = 4 * 1024 * 1024;

    private readonly string _path;

    public ClipboardStore() : this(AppPaths.FilePath("clipboard-pinned.json"))
    {
    }

    /// <summary>Lets tests point at a scratch file instead of the real app data directory.</summary>
    public ClipboardStore(string path)
    {
        _path = path;
    }

    public List<ClipboardEntry> Load()
    {
        if (!File.Exists(_path))
            return [];

        try
        {
            var json = File.ReadAllText(_path);
            var records = JsonSerializer.Deserialize<List<ClipboardRecord>>(json) ?? [];

            return records.Select(r => r.ToEntry()).OfType<ClipboardEntry>().ToList();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public void Save(IEnumerable<ClipboardEntry> pinned)
    {
        var records = pinned
            .Where(e => e.ByteCost <= MaxPinnedImageBytes)
            .Take(MaxPinned)
            .Select(ClipboardRecord.From)
            .ToList();

        var json = JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_path, json);
    }
}

/// <summary>
/// The on-disk shape of a pinned entry.
///
/// A separate type rather than serialising <see cref="ClipboardEntry"/> itself, because that one is
/// built through factories that derive a description and a signature from the payload -- the
/// derivations are the whole point of it, and a deserialiser that filled the fields in directly
/// would be free to write down a pair that disagree.
/// </summary>
public sealed record ClipboardRecord(
    ClipboardKind Kind,
    DateTime CapturedAt,
    string Text,
    string? ImageBase64,
    int Width,
    int Height,
    List<string> Paths)
{
    public static ClipboardRecord From(ClipboardEntry entry) => new(
        entry.Kind,
        entry.CapturedAt,
        entry.Text,
        entry.ImagePng is { Length: > 0 } png ? Convert.ToBase64String(png) : null,
        entry.Width,
        entry.Height,
        entry.Paths.ToList());

    /// <summary>
    /// Back to an entry, or null for a record that cannot make one. Hand-edited or half-written
    /// JSON is a missing pin, never a crash on startup.
    /// </summary>
    public ClipboardEntry? ToEntry()
    {
        try
        {
            return Kind switch
            {
                ClipboardKind.Image when ImageBase64 is { Length: > 0 } encoded =>
                    ClipboardEntry.ForImage(Convert.FromBase64String(encoded), Width, Height, CapturedAt),

                ClipboardKind.Files when Paths.Count > 0 =>
                    ClipboardEntry.ForFiles(Paths, CapturedAt),

                ClipboardKind.Text when Text.Length > 0 =>
                    ClipboardEntry.ForText(Text, CapturedAt),

                _ => null
            };
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
