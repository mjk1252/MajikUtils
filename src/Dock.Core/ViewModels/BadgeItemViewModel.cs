using CommunityToolkit.Mvvm.ComponentModel;

namespace Dock.Core.ViewModels;

/// <summary>
/// One badged app, as a chip on the collapsed pill: its icon, and how many things are waiting.
///
/// The icon rather than a colour swatch, which was the other idea and the worse one. Two of the
/// apps most likely to be badged at once -- Discord and Outlook -- are both blue, and at the eight
/// or so pixels a chip can spare there is no telling a blurple dot from a blue one. An icon is
/// unambiguous at that size because it is what the eye already learned to read off the taskbar.
/// </summary>
public sealed partial class BadgeItemViewModel : ObservableObject
{
    /// <summary>What the taskbar calls this app. The tooltip, and the fallback when there is no icon.</summary>
    public required string AppName { get; init; }

    /// <summary>How the shell knows it, and what the icon was fetched against.</summary>
    public required string AppUserModelId { get; init; }

    [ObservableProperty] private int _count;

    /// <summary>
    /// The number on the chip, or empty for a badge that carries no number.
    ///
    /// Empty rather than "1", even though a wordless badge counts as one thing waiting elsewhere.
    /// The icon alone already says "this app has something"; printing a 1 beside it claims a
    /// precision Windows never gave us, and would read as one message when it might be thirty.
    /// </summary>
    public string CountText => Count > 0 ? Count.ToString() : string.Empty;

    /// <summary>Whether there is a number worth drawing beside the icon.</summary>
    public bool HasNumber => Count > 0;

    /// <summary>
    /// The app's icon as PNG bytes. Bytes rather than an image, because this project has no WPF in
    /// it -- the view turns it into an ImageSource, the same way every other icon here is handled.
    ///
    /// Null for an app whose id resolves to nothing, which happens: Steam's taskbar button
    /// publishes an id the Applications folder has never heard of. The chip falls back to a glyph.
    /// </summary>
    public byte[]? IconPng { get; init; }

    partial void OnCountChanged(int value)
    {
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(HasNumber));
    }
}
