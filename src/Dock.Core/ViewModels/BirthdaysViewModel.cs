using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Dock.Core.Models;

namespace Dock.Core.ViewModels;

/// <summary>
/// The Birthdays scope: everybody in the file, soonest first, each with how long until theirs.
///
/// Sorted by how far away rather than by date, which is the same list rotated to start at today --
/// and is the only order that makes the panel answer the question it is opened to answer. A list
/// running January to December puts whoever is next somewhere in the middle of it.
/// </summary>
public sealed partial class BirthdaysViewModel : ObservableObject
{
    private DateOnly _today;

    /// <summary>Everyone, soonest first. What the panel scrolls.</summary>
    public ObservableCollection<BirthdayItemViewModel> Upcoming { get; } = [];

    /// <summary>
    /// Whether the file has anything readable in it, so the panel can tell an empty list from a
    /// list that has not loaded -- they look identical and mean opposite things.
    /// </summary>
    [ObservableProperty] private bool _isEmpty = true;

    /// <summary>
    /// The next one, for the panel's header line. Null on an empty list, and never null on a
    /// non-empty one: there is always a next birthday, since the list wraps into next year.
    /// </summary>
    [ObservableProperty] private BirthdayItemViewModel? _next;

    /// <summary>
    /// Rebuilds against a list and a date.
    ///
    /// Rebuilt wholesale rather than merged. The list is tens of items, it changes when a file is
    /// saved or a day turns, and every row's text depends on the date -- so there is no incremental
    /// update here that is not just a slower version of this.
    /// </summary>
    public void Apply(IReadOnlyList<Birthday> birthdays, DateOnly today)
    {
        _today = today;

        Upcoming.Clear();

        // Ties broken by name, so two people sharing a day come out in the same order every time
        // rather than in whatever order the file happened to list them after an edit.
        var ordered = birthdays
            .Select(b => new BirthdayItemViewModel(b, today))
            .OrderBy(b => b.DaysUntil)
            .ThenBy(b => b.Name, StringComparer.CurrentCultureIgnoreCase);

        foreach (var item in ordered)
            Upcoming.Add(item);

        IsEmpty = Upcoming.Count == 0;
        Next = Upcoming.FirstOrDefault();
    }

    /// <summary>
    /// Re-reads the date, rebuilding only when it has actually changed. Rides the same tick as
    /// everything else on the island, so a machine left open overnight does not spend the morning
    /// telling you a birthday is tomorrow.
    /// </summary>
    public void Tick(IReadOnlyList<Birthday> birthdays, DateOnly today)
    {
        if (today != _today)
            Apply(birthdays, today);
    }
}
