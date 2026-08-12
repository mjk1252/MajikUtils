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
    /// Whether the island shows which application is holding the microphone or the camera.
    /// Defaults on, and stays on for settings files written before it existed -- an absent property
    /// keeps the initialiser.
    /// </summary>
    public bool ShowPrivacyIndicator { get; set; } = true;

    /// <summary>Whether the island is drawn as a notch fused to the screen edge, or a free pill.</summary>
    public IslandShape IslandShape { get; set; } = IslandShape.Notch;

    /// <summary>Which end of the top edge the island sits at.</summary>
    public IslandAlignment IslandAlignment { get; set; } = IslandAlignment.Center;

    /// <summary>
    /// Adapter device name (<c>\\.\DISPLAY1</c>) of the screen the island hangs from. Empty means
    /// the primary, which is also what an unplugged monitor falls back to.
    /// </summary>
    public string IslandMonitor { get; set; } = "";

    /// <summary>
    /// How large each panel was last left, keyed by its AppUserModelID. Size only, not position:
    /// panels place themselves against the taskbar button that opened them, on whichever monitor
    /// that button is on, so there is no position worth carrying across sessions.
    /// </summary>
    public Dictionary<string, PanelSize> PanelSizes { get; set; } = new();

    /// <summary>
    /// Whether copies, downloads, screenshots, drive and network changes, volume and Bluetooth
    /// surface as an announcement on the island. Defaults on, and stays on for settings files
    /// written before it existed.
    /// </summary>
    public bool ShowAnnouncements { get; set; } = true;

    /// <summary>
    /// Whether do-not-disturb, a pending restart, low disk space and low battery show as a dot on
    /// the island. Defaults on, and stays on for settings files written before it existed.
    /// </summary>
    public bool ShowConditions { get; set; } = true;

    /// <summary>
    /// Whether an application making sound can claim the island on its own. The Mixer tab works
    /// either way -- this only governs whether it can interrupt the ambient pill uninvited.
    /// </summary>
    public bool ShowVolumeMixer { get; set; } = true;

    /// <summary>Opens the island on its Clipboard tab. Ctrl+Alt+Shift+V by default.</summary>
    public HotkeyBinding ClipboardHotkey { get; set; } = new(modifiers: 0x2 | 0x1 | 0x4, key: 0x56);

    /// <summary>Opens the command palette. Ctrl+Alt+Space by default.</summary>
    public HotkeyBinding PaletteHotkey { get; set; } = new(modifiers: 0x2 | 0x1, key: 0x20);
}

public enum IslandShape
{
    /// <summary>Fused to the screen edge, its top corners flaring outwards to meet it.</summary>
    Notch,

    /// <summary>A detached lozenge floating a little below the edge, rounded on all four corners.</summary>
    Pill
}

public enum IslandAlignment
{
    Left,
    Center,
    Right
}

public sealed class PanelSize
{
    public double Width { get; set; }
    public double Height { get; set; }
}
