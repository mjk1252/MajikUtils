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
    /// Whether the island hangs from every attached screen, plugging and unplugging included.
    /// Overrides <see cref="IslandMonitors"/> outright rather than expanding into it, so a monitor
    /// arriving later is covered without anyone having to come back here and tick it.
    /// </summary>
    public bool IslandOnAllMonitors { get; set; }

    /// <summary>
    /// Adapter device names of every screen the island hangs from, when it is not on all of them.
    ///
    /// Empty falls back to <see cref="IslandMonitor"/> and then to the primary, which is what makes
    /// a settings file written before this existed open with the island exactly where it was.
    /// </summary>
    public List<string> IslandMonitors { get; set; } = [];

    /// <summary>
    /// Which screens the island should actually be on, given all three settings above.
    ///
    /// One place to answer it, because the fallbacks are the whole subtlety and the alternative is
    /// every caller reimplementing them slightly differently. An empty device name means "the
    /// primary, whichever that is", and a result is never empty: there is no way to ask for no
    /// island at all, and unticking the last monitor should leave it somewhere rather than lose it.
    /// </summary>
    /// <param name="attached">Device names of the screens currently plugged in, in any order.</param>
    public IReadOnlyList<string> EffectiveMonitors(IReadOnlyList<string> attached)
    {
        ArgumentNullException.ThrowIfNull(attached);

        if (IslandOnAllMonitors)
            return attached.Count > 0 ? attached : [""];

        // Only the ones still plugged in. A saved screen that has since been unplugged is not an
        // error and not a reason to fall back -- the others are still perfectly good answers.
        var chosen = IslandMonitors
            .Where(m => attached.Contains(m, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (chosen.Count > 0)
            return chosen;

        // Nothing chosen, or nothing chosen is still plugged in. The single-monitor setting is the
        // older way of saying the same thing, and an empty string is "follow the primary".
        return [IslandMonitor ?? ""];
    }

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

    /// <summary>
    /// Whether the time shows on the collapsed island. Defaults on, and stays on for settings
    /// files written before it existed.
    ///
    /// On, the island stays on screen whenever the clock is the only thing it has to say -- which
    /// is the point of it. A clock that hid until you went looking would be no better than the
    /// auto-hidden taskbar it is standing in for.
    /// </summary>
    public bool ShowClock { get; set; } = true;

    /// <summary>
    /// Whether the island shows what the taskbar's own buttons are badged with. Defaults on, and
    /// stays on for settings files written before it existed.
    ///
    /// The other half of standing in for a hidden taskbar: the clock covers what it told you at a
    /// glance, this covers what it was trying to get your attention about.
    /// </summary>
    public bool ShowTaskbarBadges { get; set; } = true;

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
