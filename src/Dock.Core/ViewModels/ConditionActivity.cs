using CommunityToolkit.Mvvm.ComponentModel;

namespace Dock.Core.ViewModels;

/// <summary>
/// A standing condition: true for long stretches, worth a dot, never worth the pill.
///
/// Focus mode being on, a restart being pending. The opposite shape to an announcement -- that one
/// is an instant that has to be caught, this one is a state that is simply the case -- and the
/// priority ladder is what keeps them out of each other's way.
///
/// One class, several instances: what differs between "Do not disturb" and "Restart pending" is a
/// label and a glyph, and a class each would be two copies of the same eight lines.
/// </summary>
public sealed partial class ConditionActivity : ObservableObject, IIslandActivity
{
    [ObservableProperty] private bool _isActive;

    /// <summary>
    /// Shown when this holds the pill, which only happens with nothing else on the island.
    ///
    /// Settable rather than fixed at construction: most conditions are simply on or off, but a
    /// battery running down is the same condition reading differently as it goes.
    /// </summary>
    [ObservableProperty] private string _label = string.Empty;

    public required string Key { get; init; }

    /// <summary>A Segoe Fluent Icons glyph, and the whole of the compact form.</summary>
    public required string Glyph { get; init; }

    /// <summary>
    /// Never above music. A condition that is usually true must not take the pill off something
    /// that is actually happening -- which is the lesson the camera indicator taught.
    /// </summary>
    public IslandPriority Priority => IslandPriority.Background;

    /// <summary>
    /// Brief. These are polled rather than pushed, so a reading can flicker between two ticks, but
    /// the underlying state changes on the order of minutes and does not need long protection.
    /// </summary>
    public TimeSpan Linger => TimeSpan.FromMilliseconds(750);

    public void Retire()
    {
        // The label and glyph are fixed for the life of the activity; there is nothing to clear.
    }
}
