namespace Dock.Core.Models;

/// <summary>
/// Decides whether the pointer being over the island counts as somebody looking at it.
///
/// It used to be the bare question -- is the cursor inside the region, yes or no -- and that is
/// wrong at the top edge of a screen. The strip that summons a hidden island is three pixels tall
/// and has no slack around it, so a pointer travelling along the top edge crosses it in passing:
/// between two monitors, up to a maximised window's tab strip, anywhere. Each crossing brought the
/// island out and put it away again, and doing that four times while moving the mouse across the
/// screen is what "it flickers on its own" turned out to be. The user was not going anywhere near
/// it deliberately, which is exactly why it looked like the island doing it unprompted.
///
/// A hover is somebody stopping. A transit is somebody passing through. The difference between
/// them is time, so the gate only changes its mind once the pointer has held still about it --
/// which costs a deliberate hover a fraction of a second and costs a transit nothing at all,
/// because a transit never lasts that long.
/// </summary>
public sealed class HoverGate
{
    /// <summary>
    /// How long the pointer has to agree with itself before the island believes it.
    ///
    /// Short enough not to feel like lag on a hover somebody meant, long enough that a pointer
    /// crossing a 260-pixel strip on the way somewhere else never qualifies. A pointer moving at
    /// any ordinary speed clears that strip in well under this.
    /// </summary>
    public static readonly TimeSpan Dwell = TimeSpan.FromMilliseconds(180);

    private bool _open;
    private bool? _pending;
    private DateTimeOffset _pendingSince;

    /// <summary>Whether the island should currently treat the pointer as on it.</summary>
    public bool IsOpen => _open;

    /// <summary>
    /// Offers the raw answer and returns the settled one.
    /// </summary>
    /// <param name="inside">Whether the pointer is in the island's region right now.</param>
    public bool Update(DateTimeOffset now, bool inside)
    {
        // Already agrees: nothing pending, nothing to time.
        if (inside == _open)
        {
            _pending = null;
            return _open;
        }

        // A change of mind restarts the clock rather than accumulating -- a pointer flickering
        // either side of the boundary must not add its moments together into a decision.
        if (_pending != inside)
        {
            _pending = inside;
            _pendingSince = now;
            return _open;
        }

        if (now - _pendingSince < Dwell)
            return _open;

        _open = inside;
        _pending = null;

        return _open;
    }

    /// <summary>
    /// Forces the gate open without waiting, for the island being opened by something other than
    /// the pointer -- a hotkey, a pinned shortcut. Those are not hovers and have nothing to prove.
    /// </summary>
    public void ForceOpen(DateTimeOffset now)
    {
        _open = true;
        _pending = null;
        _pendingSince = now;
    }
}
