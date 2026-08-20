namespace Dock.Core.Models;

/// <summary>
/// How much of the clipboard history has to go to fit a memory budget.
///
/// A function rather than a loop inside the view model, because it is the one piece of the history
/// with a rule worth stating: it is not "drop until it fits", it is "drop until it fits, but never
/// the thing that was just copied". Getting that wrong turns a copy that was slightly too big into
/// a copy that silently did nothing, which is the worst outcome available -- the user has already
/// destroyed whatever was on the clipboard before it.
/// </summary>
public static class ClipboardBudget
{
    /// <summary>
    /// Which entries to drop from a newest-first history so the total fits, oldest first.
    ///
    /// Indices rather than a count, because two things are now exempt and neither of them is at a
    /// predictable end of the list: the newest entry, and anything pinned.
    /// </summary>
    public static IReadOnlyList<int> Excess(IReadOnlyList<ClipboardCost> newestFirst, long budget)
    {
        var total = 0L;
        foreach (var entry in newestFirst)
            total += entry.Cost;

        var drop = new List<int>();

        // Walks backwards from the oldest, stopping one short of the front: a single entry larger
        // than the whole budget is kept rather than dropped into nothing.
        for (var i = newestFirst.Count - 1; i > 0 && total > budget; i--)
        {
            // A pin is the user saying "keep this one". Evicting it to make room for something they
            // did not ask to keep gets the priority exactly backwards.
            if (newestFirst[i].Pinned)
                continue;

            total -= newestFirst[i].Cost;
            drop.Add(i);
        }

        return drop;
    }
}

/// <summary>What the budget needs to know about one entry: what it costs, and whether it is spoken for.</summary>
public readonly record struct ClipboardCost(long Cost, bool Pinned);
