using Dock.Core.Models;

namespace Dock.Core.Tests;

/// <summary>
/// Folding the CSV and the calendar into one list. One row per person, and the row that knows the
/// year wins.
/// </summary>
public class BirthdayMergeTests
{
    [Fact]
    public void TheSamePersonInBothSources_AppearsOnce()
    {
        var merged = BirthdayMerge.Combine(
            [new Birthday("Tom", 8, 22, 1990)],
            [new Birthday("Tom", 8, 22, null)]);

        var only = Assert.Single(merged);
        Assert.Equal("Tom", only.Name);

        // The CSV knew the year, so the merged row can still work out an age.
        Assert.Equal(1990, only.Year);
    }

    /// <summary>
    /// Order-independent: the calendar arriving first must not leave the list without the birth
    /// year the file had.
    /// </summary>
    [Fact]
    public void TheYearSurvives_WhicheverSourceCameFirst()
    {
        var merged = BirthdayMerge.Combine(
            [new Birthday("Tom", 8, 22, null)],
            [new Birthday("Tom", 8, 22, 1990)]);

        Assert.Equal(1990, Assert.Single(merged).Year);
    }

    [Fact]
    public void NamesMatchIgnoringCaseAndSpace()
    {
        var merged = BirthdayMerge.Combine(
            [new Birthday("tom", 8, 22, null)],
            [new Birthday("  TOM  ", 8, 22, null)]);

        Assert.Single(merged);
    }

    /// <summary>Two people who happen to share a name are two birthdays, not one.</summary>
    [Fact]
    public void TheSameNameOnDifferentDays_StaysTwoEntries()
    {
        var merged = BirthdayMerge.Combine(
            [new Birthday("Tom", 8, 22, null)],
            [new Birthday("Tom", 3, 14, null)]);

        Assert.Equal(2, merged.Count);
    }

    /// <summary>
    /// It deliberately does not guess that two spellings are one person -- that is how a merge
    /// starts hiding entries somebody wrote down on purpose.
    /// </summary>
    [Fact]
    public void DifferentSpellings_AreNotGuessedTogether()
    {
        var merged = BirthdayMerge.Combine(
            [new Birthday("Tom", 8, 22, null)],
            [new Birthday("Tom Smith", 8, 22, null)]);

        Assert.Equal(2, merged.Count);
    }

    [Fact]
    public void InvalidEntries_AreDropped()
    {
        var merged = BirthdayMerge.Combine(
            [new Birthday("Tom", 8, 22, null), new Birthday("", 8, 22, null)],
            [new Birthday("Bad", 13, 40, null)]);

        Assert.Equal("Tom", Assert.Single(merged).Name);
    }

    [Fact]
    public void EitherSourceBeingEmpty_IsFine()
    {
        Assert.Single(BirthdayMerge.Combine([new Birthday("Tom", 8, 22, null)], []));
        Assert.Single(BirthdayMerge.Combine([], [new Birthday("Tom", 8, 22, null)]));
        Assert.Empty(BirthdayMerge.Combine([], []));
    }

    /// <summary>The file's order is kept, with anything new from the calendar appended after it.</summary>
    [Fact]
    public void TheFilesOrderIsKept()
    {
        var merged = BirthdayMerge.Combine(
            [new Birthday("Ada", 12, 10, null), new Birthday("Tom", 8, 22, null)],
            [new Birthday("Sarah", 1, 5, null)]);

        Assert.Equal(["Ada", "Tom", "Sarah"], merged.Select(b => b.Name));
    }
}
