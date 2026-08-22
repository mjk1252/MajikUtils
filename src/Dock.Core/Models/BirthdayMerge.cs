namespace Dock.Core.Models;

/// <summary>
/// Folds the two sources of birthdays -- the CSV kept by hand and a subscribed calendar -- into the
/// one list the island and the scope both read.
///
/// One row per person, because two sources describing the same fact is a detail of where the data
/// came from and not something anybody wants to see twice on the day. The merge is by name and
/// date rather than by name alone: two different people called Tom are two birthdays, and the same
/// Tom appearing in both sources is one.
/// </summary>
public static class BirthdayMerge
{
    /// <summary>
    /// The CSV first, then the calendar, deduplicated.
    ///
    /// Where the same person appears in both, the entry carrying a birth year wins -- which is
    /// almost always the CSV one, since a calendar event's year is the year of the event rather
    /// than of the birth and is deliberately dropped on the way in. That is the whole reason to
    /// prefer one over the other: they are the same fact, and one of the two can work out an age.
    /// </summary>
    public static List<Birthday> Combine(
        IReadOnlyList<Birthday> fromFile, IReadOnlyList<Birthday> fromCalendar)
    {
        ArgumentNullException.ThrowIfNull(fromFile);
        ArgumentNullException.ThrowIfNull(fromCalendar);

        var merged = new List<Birthday>(fromFile.Count + fromCalendar.Count);
        var seen = new Dictionary<(string Name, int Month, int Day), int>(KeyComparer.Instance);

        foreach (var birthday in fromFile.Concat(fromCalendar))
        {
            if (!birthday.IsValid)
                continue;

            var key = (birthday.Name.Trim(), birthday.Month, birthday.Day);

            if (!seen.TryGetValue(key, out var index))
            {
                seen[key] = merged.Count;
                merged.Add(birthday);
                continue;
            }

            // Already have this person on this day. Keep whichever knows the year; if the one held
            // already does, the newcomer has nothing to add.
            if (merged[index].Year is null && birthday.Year is not null)
                merged[index] = birthday;
        }

        return merged;
    }

    /// <summary>
    /// Names match case-insensitively and ignoring surrounding space, because one source is typed
    /// by hand and the other comes from a calendar box typed by hand on a phone. It does not try to
    /// match "Tom" against "Tom Smith" -- guessing that two different spellings are one person is
    /// how a merge starts hiding entries somebody deliberately wrote down.
    /// </summary>
    private sealed class KeyComparer : IEqualityComparer<(string Name, int Month, int Day)>
    {
        public static readonly KeyComparer Instance = new();

        public bool Equals((string Name, int Month, int Day) x, (string Name, int Month, int Day) y) =>
            x.Month == y.Month && x.Day == y.Day &&
            string.Equals(x.Name, y.Name, StringComparison.CurrentCultureIgnoreCase);

        public int GetHashCode((string Name, int Month, int Day) key) =>
            HashCode.Combine(key.Name.ToLower(System.Globalization.CultureInfo.CurrentCulture),
                key.Month, key.Day);
    }
}
