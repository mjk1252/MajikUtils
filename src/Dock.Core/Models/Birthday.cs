namespace Dock.Core.Models;

/// <summary>
/// Somebody's birthday, as a day of the year rather than a date.
///
/// The year is optional and means only one thing: whether an age can be worked out. A list kept by
/// hand is mostly people whose birthday you know and whose year you do not, and demanding one would
/// turn a two-word line into a small research task -- so it is a nullable field rather than a
/// required one, and everything that depends on it is nullable in turn.
///
/// Deliberately not a <c>DateOnly</c>. A birthday is not an instant, it is a rule for picking one
/// out of every year, and storing the original date would mean every reader re-deriving the rule.
/// </summary>
public sealed record Birthday(string Name, int Month, int Day, int? Year)
{
    /// <summary>
    /// Whether this describes a day that exists. Guards the parser rather than the constructor:
    /// a record with validation in its constructor throws while reading a file the user typed by
    /// hand, and one bad line should cost that line rather than the whole list.
    /// </summary>
    public bool IsValid =>
        Name.Length > 0 &&
        Month is >= 1 and <= 12 &&
        Day >= 1 &&
        // Against a leap year, so 29 February is a birthday somebody is allowed to have.
        Day <= DateTime.DaysInMonth(2024, Month);

    /// <summary>
    /// The next date this falls on, counting today as the next one.
    ///
    /// Counting today is the whole point: this drives a countdown that has to read "today" on the
    /// day, and a "next occurrence" that skipped to next year the moment the day arrived would
    /// hide the one birthday anybody cares about.
    /// </summary>
    public DateOnly NextOccurrence(DateOnly today)
    {
        var thisYear = InYear(today.Year);
        return thisYear >= today ? thisYear : InYear(today.Year + 1);
    }

    /// <summary>
    /// This birthday as it falls in a given year.
    ///
    /// 29 February is the only day that needs a rule, and the rule is 28 February: a leapling's
    /// birthday moves to the last day of the month they were born in rather than the first day of
    /// the next one. Either convention is defensible; this one keeps the date inside February,
    /// which is what people who have this birthday tend to say they do.
    /// </summary>
    public DateOnly InYear(int year)
    {
        var day = Math.Min(Day, DateTime.DaysInMonth(year, Month));
        return new DateOnly(year, Month, day);
    }

    /// <summary>How many days away the next one is. Zero on the day.</summary>
    public int DaysUntil(DateOnly today) => NextOccurrence(today).DayNumber - today.DayNumber;

    /// <summary>Whether this falls on the given date.</summary>
    public bool IsOn(DateOnly date) => InYear(date.Year) == date;

    /// <summary>
    /// The age reached on the next occurrence, or null when the year was left out.
    ///
    /// The age they are *turning*, not the age they are. On the day itself those are the same
    /// number, which is the day this is for.
    /// </summary>
    public int? TurningAge(DateOnly today) =>
        Year is { } year ? NextOccurrence(today).Year - year : null;
}
