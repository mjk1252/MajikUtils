using System.Globalization;
using Dock.Core.Models;

namespace Dock.Core.ViewModels;

/// <summary>
/// One person in the birthday list, worked out against a particular day.
///
/// Immutable, and built fresh whenever the list or the date changes rather than kept live. Every
/// field here is a function of (birthday, today) and the second of those moves once a day -- a
/// mutable row would need every one of them re-raised at midnight for no benefit over rebuilding
/// a list that is thirty items long at the outside.
/// </summary>
public sealed class BirthdayItemViewModel
{
    public BirthdayItemViewModel(Birthday birthday, DateOnly today)
    {
        Birthday = birthday;
        DaysUntil = birthday.DaysUntil(today);
        TurningAge = birthday.TurningAge(today);
        Next = birthday.NextOccurrence(today);
    }

    public Birthday Birthday { get; }

    public string Name => Birthday.Name;

    /// <summary>Days from the day this was built against. Zero today.</summary>
    public int DaysUntil { get; }

    public bool IsToday => DaysUntil == 0;

    /// <summary>The date it next falls on, which is today's when it is today.</summary>
    public DateOnly Next { get; }

    /// <summary>The age reached on <see cref="Next"/>, or null when the year was left out.</summary>
    public int? TurningAge { get; }

    public bool HasAge => TurningAge is not null;

    /// <summary>"14 March" -- the day itself, without a year nobody needs on a list of upcoming ones.</summary>
    public string DateText => Next.ToString("d MMMM", CultureInfo.CurrentCulture);

    /// <summary>
    /// The countdown, in the units a person would use out loud.
    ///
    /// Days all the way up to a year would read "in 287 days", which is a number nobody converts
    /// into a feeling about how soon it is. Weeks and months are what the far end of a list like
    /// this is actually for -- it answers "is this coming up" rather than "exactly when".
    /// </summary>
    public string CountdownText => DaysUntil switch
    {
        0 => "Today",
        1 => "Tomorrow",
        < 14 => $"In {DaysUntil} days",
        < 60 => $"In {DaysUntil / 7} weeks",
        _ => $"In {DaysUntil / 30} months"
    };

    /// <summary>The age line for the row, when there is one: "turns 47".</summary>
    public string AgeText => TurningAge is { } age ? $"turns {age}" : string.Empty;
}
