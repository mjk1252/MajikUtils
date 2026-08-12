namespace Dock.Core.Models;

/// <summary>
/// A global hotkey, as the numbers <c>RegisterHotKey</c> takes -- <see cref="Modifiers"/> is a
/// bitmask of MOD_ALT/MOD_CONTROL/MOD_SHIFT/MOD_WIN and <see cref="Key"/> a virtual-key code.
///
/// Stored as raw numbers rather than a WPF <c>Key</c>/<c>ModifierKeys</c> pair so this can sit in
/// <see cref="AppSettings"/> without pulling System.Windows into a project that never references
/// Win32 or WPF -- <c>Dock.App</c> is the one place that ever has to translate between the two.
/// </summary>
public sealed class HotkeyBinding
{
    public uint Modifiers { get; set; }
    public uint Key { get; set; }

    public HotkeyBinding() { }

    public HotkeyBinding(uint modifiers, uint key)
    {
        Modifiers = modifiers;
        Key = key;
    }
}
