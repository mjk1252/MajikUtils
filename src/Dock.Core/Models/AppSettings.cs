namespace Dock.Core.Models;

public sealed class AppSettings
{
    public bool HideTaskbar { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public DockPosition Position { get; set; } = DockPosition.Bottom;
}
