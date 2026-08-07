using System.ComponentModel;

namespace Dock.Core.ViewModels;

/// <summary>
/// Something that can claim the island's collapsed pill.
///
/// Deliberately tiny. An activity says whether it wants the pill and how badly, and everything
/// about how it *looks* is a DataTemplate keyed on the concrete view model: an activity that had
/// to describe itself as a title and a glyph could not express the now-playing bars, and a shape
/// general enough for both would leave each one worse than the bespoke visual it replaced.
///
/// Nothing here mentions colour either. <c>ArtworkAccent</c> needs WPF imaging and so cannot live
/// in this project, and pushing a packed-ARGB struct through the interface only to hand it back to
/// the view buys nothing -- the accent is a template's business.
/// </summary>
public interface IIslandActivity : INotifyPropertyChanged
{
    /// <summary>Stable identity, for diagnostics and for keeping one registration per source.</summary>
    string Key { get; }

    /// <summary>Whether this wants the pill right now.</summary>
    bool IsActive { get; }

    IslandPriority Priority { get; }

    /// <summary>
    /// How long this keeps its slot after going inactive.
    ///
    /// Sources at this level flap: a player restarts its session between albums, and a
    /// conferencing app releases and reacquires the microphone several times a call. The island is
    /// a strip of screen the user may well be looking at when it happens, and lingering is what
    /// stops either from tearing it down and rebuilding it a moment later.
    ///
    /// Zero for an activity that should go the instant it says so.
    /// </summary>
    TimeSpan Linger { get; }

    /// <summary>
    /// Called by the host once the linger window has closed and this activity is genuinely off the
    /// island. Where an activity clears its display state.
    ///
    /// Which is emphatically *not* when <see cref="IsActive"/> goes false. An activity that blanked
    /// itself on that edge would leave the pill showing an empty version of itself for the whole
    /// grace period the linger was meant to cover -- the gap between two tracks would read as
    /// "nothing playing" rather than as the track that is about to start.
    /// </summary>
    void Retire();
}

/// <summary>
/// What outranks what when several activities want the pill at once.
///
/// Explicit values rather than an ordinal, so a new activity can be slotted between two existing
/// ones without renumbering the file.
/// </summary>
public enum IslandPriority
{
    /// <summary>The resting state -- what the pill shows when nothing else is happening.</summary>
    Ambient = 0,

    /// <summary>A condition worth interrupting the ambient state for.</summary>
    Status = 100,

    /// <summary>Something with an end: a timer, a transfer.</summary>
    Transient = 200,

    Alert = 300
}
