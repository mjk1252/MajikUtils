using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Core.Models;
using Dock.Core.Services;

namespace Dock.Core.ViewModels;

public partial class ClipboardEntryViewModel : ObservableObject
{
    /// <summary>
    /// How many file names a row spells out before it starts counting instead. Three fits the row
    /// without wrapping; a copy of forty files is a number, not a list.
    /// </summary>
    private const int NamedFiles = 3;

    private readonly IClipboardWriter _writer;
    private readonly Action? _pinChanged;

    public ClipboardEntry Entry { get; }

    /// <summary>
    /// Whether this entry is exempt from being evicted, and written to disk between sessions.
    ///
    /// State on the view model rather than the entry, because it is the one thing about a clipboard
    /// entry that changes after it was captured -- everything else is a record of a moment.
    /// </summary>
    [ObservableProperty] private bool _isPinned;

    partial void OnIsPinnedChanged(bool value) => _pinChanged?.Invoke();

    public string Text => Entry.Text;

    public ClipboardKind Kind => Entry.Kind;

    // Three flags rather than one enum for the row to switch on: WPF has no enum-equality trigger
    // without a converter, and three bools bind straight to the visibility converter every other
    // panel already uses.
    public bool IsText => Entry.Kind == ClipboardKind.Text;

    public bool IsImage => Entry.Kind == ClipboardKind.Image;

    public bool IsFiles => Entry.Kind == ClipboardKind.Files;

    /// <summary>The PNG the row draws, for an image entry.</summary>
    public byte[]? ImagePng => Entry.ImagePng;

    /// <summary>
    /// Icons for the named files, filled in by whoever owns an <see cref="IIconProvider"/> --
    /// the same arrangement <see cref="ShelfItemViewModel"/> uses, and for the same reason: pulling
    /// an icon out of the shell is not something Dock.Core can do.
    /// </summary>
    public ObservableCollection<ClipboardFileViewModel> Files { get; } = [];

    /// <summary>How many paths there are beyond the ones <see cref="Files"/> names.</summary>
    public int ExtraFileCount => Math.Max(0, Entry.Paths.Count - NamedFiles);

    /// <summary>One line of words for any entry, whatever it holds.</summary>
    public string Preview => Entry.Kind == ClipboardKind.Text ? BuildPreview(Entry.Text) : Entry.Text;

    public ClipboardEntryViewModel(ClipboardEntry entry, IClipboardWriter writer,
        Action? pinChanged = null, bool isPinned = false)
    {
        Entry = entry;
        _writer = writer;
        _pinChanged = pinChanged;

        // Set through the field so restoring a pinned entry at startup does not call back into the
        // store that just loaded it.
        _isPinned = isPinned;

        foreach (var path in entry.Paths.Take(NamedFiles))
            Files.Add(new ClipboardFileViewModel(path));
    }

    /// <summary>
    /// Whether this entry answers a search.
    ///
    /// Against the whole payload, not the preview. The preview is truncated at 140 characters, and
    /// searching that meant a copied page could not be found by any word past the first line or
    /// two -- which is exactly the entry a search is for. What a row *shows* being what you can
    /// find sounds tidy right up against the case where the row shows a hundredth of it.
    /// </summary>
    public bool Matches(string query) =>
        Entry.Text.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        Entry.Paths.Any(p => p.Contains(query, StringComparison.OrdinalIgnoreCase));

    private static string BuildPreview(string text)
    {
        var collapsed = string.Join(" ", text.Split(
            ['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();

        if (string.IsNullOrEmpty(collapsed))
            collapsed = text.Trim();

        const int maxLength = 140;
        return collapsed.Length > maxLength ? collapsed[..maxLength] + "…" : collapsed;
    }

    /// <summary>
    /// Puts this entry back, in the form it was taken in. An image copied out as its own dimensions
    /// and files copied out as a real drop list are the whole point of holding either: a screenshot
    /// that came back as the words "Image 1920 x 1080" would be a worse feature than not keeping it.
    /// </summary>
    [RelayCommand]
    private void TogglePin() => IsPinned = !IsPinned;

    [RelayCommand]
    private void Copy()
    {
        switch (Entry.Kind)
        {
            case ClipboardKind.Image when Entry.ImagePng is { Length: > 0 } png:
                _writer.SetImage(png);
                break;

            case ClipboardKind.Files when Entry.Paths.Count > 0:
                _writer.SetFiles(Entry.Paths);
                break;

            default:
                _writer.SetText(Entry.Text);
                break;
        }
    }
}

/// <summary>One named file inside a files entry: what the row draws per path.</summary>
public partial class ClipboardFileViewModel(string path) : ObservableObject
{
    public string Path => path;

    public string Name => System.IO.Path.GetFileName(path) is { Length: > 0 } name
        ? name
        : path;

    [ObservableProperty] private byte[]? _iconPng;
}
