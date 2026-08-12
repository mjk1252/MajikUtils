using System.Windows.Input;

namespace Dock.Core.ViewModels;

/// <summary>
/// One row in the command palette, wrapping whichever source it came from behind one shape.
///
/// Deliberately thin: it does not launch an app or copy a clipboard entry itself, it carries the
/// command that already does. <see cref="AppLauncherItemViewModel.LaunchCommand"/>,
/// <see cref="RecentFileItemViewModel.OpenCommand"/> and <see cref="ClipboardEntryViewModel.CopyCommand"/>
/// are the same commands their own panels bind to -- the palette does not reimplement what any of
/// them already do, only ranks and displays them together.
/// </summary>
public sealed class PaletteItemViewModel(string title, string subtitle, byte[]? iconPng, string category, ICommand activateCommand)
{
    public string Title { get; } = title;
    public string Subtitle { get; } = subtitle;
    public byte[]? IconPng { get; } = iconPng;
    public string Category { get; } = category;
    public ICommand ActivateCommand { get; } = activateCommand;
}
