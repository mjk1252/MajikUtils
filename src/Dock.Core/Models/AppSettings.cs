namespace Dock.Core.Models;

public sealed class AppSettings
{
    public bool StartWithWindows { get; set; }

    /// <summary>
    /// Whether the media island hangs from the top of the primary monitor. Defaults on, and stays
    /// on for settings files written before it existed -- an absent property keeps the initialiser.
    /// </summary>
    public bool ShowMediaIsland { get; set; } = true;

    /// <summary>
    /// How large each panel was last left, keyed by its AppUserModelID. Size only, not position:
    /// panels place themselves against the taskbar button that opened them, on whichever monitor
    /// that button is on, so there is no position worth carrying across sessions.
    /// </summary>
    public Dictionary<string, PanelSize> PanelSizes { get; set; } = new();
}

public sealed class PanelSize
{
    public double Width { get; set; }
    public double Height { get; set; }
}
