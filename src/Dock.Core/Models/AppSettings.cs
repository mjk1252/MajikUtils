namespace Dock.Core.Models;

public sealed class AppSettings
{
    public bool StartWithWindows { get; set; }

    /// <summary>
    /// Where each panel window was last left, keyed by its AppUserModelID. Persisted because the
    /// panels are never really closed -- they minimise -- so WPF's own restore behaviour only
    /// covers a single run, and a user who drags the drawer somewhere expects it back there after
    /// a reboot.
    /// </summary>
    public Dictionary<string, PanelPlacement> PanelPlacements { get; set; } = new();
}

public sealed class PanelPlacement
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}
