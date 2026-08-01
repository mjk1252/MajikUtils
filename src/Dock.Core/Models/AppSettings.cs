namespace Dock.Core.Models;

public sealed class AppSettings
{
    public bool HideTaskbar { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public DockPosition Position { get; set; } = DockPosition.Bottom;
    public string AccentColor { get; set; } = "#1E1E1E";
    public int TintOpacity { get; set; } = 9;

    /// <summary>Fallback icon size for a monitor with no entry in <see cref="IconSizeByMonitor"/> yet.</summary>
    public double IconSize { get; set; } = 52;

    /// <summary>Per-monitor icon size overrides, keyed by each monitor's stable device name (e.g. "\\.\DISPLAY1").</summary>
    public Dictionary<string, double> IconSizeByMonitor { get; set; } = new();

    /// <summary>
    /// Padding inside the dock's glass panel, in DIPs, measured along its long axis. The cross axis
    /// (which sets how thick the bar looks) takes a third of it, so the default of 6 reproduces the
    /// original hard-coded 6,2.
    /// </summary>
    public double DockPadding { get; set; } = 6;

    /// <summary>Gap between adjacent dock icons, in DIPs, as a margin on each side of every icon.</summary>
    public double IconSpacing { get; set; } = 4;

    /// <summary>Gap in DIPs between the dock and the edge of the screen it is docked to.</summary>
    public double DockMargin { get; set; } = 12;

    /// <summary>
    /// Extra reserved space, in DIPs, between the dock's inner edge and a maximized app window --
    /// i.e. how far above the pill (bottom dock) or how far past its inner edge (left/right dock) a
    /// window's usable area starts. Independent of <see cref="DockMargin"/>, which only governs the
    /// pill's own distance from the physical screen edge.
    /// </summary>
    public double AppClearance { get; set; } = 12;
}
