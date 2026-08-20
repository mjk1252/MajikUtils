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
    /// How many entries to drop from the end of a newest-first history so the total fits.
    ///
    /// Never returns the whole count: the newest entry survives at any size.
    /// </summary>
    public static int Excess(IReadOnlyList<long> costsNewestFirst, long budget)
    {
        var total = 0L;
        foreach (var cost in costsNewestFirst)
            total += cost;

        var drop = 0;

        // Walks backwards from the oldest. Stops one short of the front by construction, so a
        // single entry larger than the whole budget is kept rather than dropped into nothing.
        for (var i = costsNewestFirst.Count - 1; i > 0 && total > budget; i--)
        {
            total -= costsNewestFirst[i];
            drop++;
        }

        return drop;
    }
}
