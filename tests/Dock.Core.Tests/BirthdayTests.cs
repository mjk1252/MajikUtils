using Dock.Core.Models;

namespace Dock.Core.Tests;

/// <summary>
/// The date arithmetic, and the file format the user types by hand.
///
/// These two are the whole of the feature's risk. Everything above them is a template and a
/// registration; what can actually be wrong is which day a birthday falls on and which lines of a
/// hand-edited CSV count as one.
/// </summary>
public class BirthdayTests
{
    private static readonly DateOnly March14 = new(2026, 3, 14);

    // ---------------------------------------------------------------- the day itself

    [Fact]
    public void Today_CountsAsTheNextOccurrence()
    {
        var birthday = new Birthday("Ada", 3, 14, 1990);

        Assert.Equal(0, birthday.DaysUntil(March14));
        Assert.True(birthday.IsOn(March14));
    }

    /// <summary>
    /// The rule the countdown is built on: a birthday that has been and gone this year is next
    /// year's. Getting this wrong gives a list sorted by a negative number.
    /// </summary>
    [Fact]
    public void APastBirthday_RollsIntoNextYear()
    {
        var birthday = new Birthday("Ada", 1, 5, null);

        Assert.Equal(new DateOnly(2027, 1, 5), birthday.NextOccurrence(March14));
        Assert.Equal(297, birthday.DaysUntil(March14));
    }

    [Fact]
    public void ALaterBirthday_StaysInThisYear()
    {
        var birthday = new Birthday("Ada", 3, 15, null);

        Assert.Equal(1, birthday.DaysUntil(March14));
    }

    /// <summary>
    /// A leapling's birthday is the 28th in an ordinary year, not the 1st of March. Either
    /// convention could have been chosen; what matters is that the date exists and that the same
    /// one comes back every time.
    /// </summary>
    [Fact]
    public void TheTwentyNinthOfFebruary_FallsBackToTheTwentyEighth()
    {
        var birthday = new Birthday("Ada", 2, 29, 2000);

        Assert.Equal(new DateOnly(2026, 2, 28), birthday.InYear(2026));
        Assert.Equal(new DateOnly(2024, 2, 29), birthday.InYear(2024));
    }

    /// <summary>29 February has to survive validation, or a real birthday is a parse error.</summary>
    [Fact]
    public void TheTwentyNinthOfFebruary_IsAValidBirthday()
    {
        Assert.True(new Birthday("Ada", 2, 29, null).IsValid);
        Assert.False(new Birthday("Ada", 2, 30, null).IsValid);
        Assert.False(new Birthday("Ada", 13, 1, null).IsValid);
        Assert.False(new Birthday("", 1, 1, null).IsValid);
    }

    // ---------------------------------------------------------------- ages

    /// <summary>The age reached on the next one, which on the day is the age they are.</summary>
    [Fact]
    public void TheAge_IsTheOneBeingTurned()
    {
        Assert.Equal(36, new Birthday("Ada", 3, 14, 1990).TurningAge(March14));

        // Already passed this year, so the next one is in 2027 and they turn 37 on it.
        Assert.Equal(37, new Birthday("Ada", 1, 5, 1990).TurningAge(March14));
    }

    [Fact]
    public void WithNoYear_ThereIsNoAge()
    {
        Assert.Null(new Birthday("Ada", 3, 14, null).TurningAge(March14));
    }

    // ---------------------------------------------------------------- the file

    [Theory]
    [InlineData("Ada,1990-03-14", "Ada", 3, 14, 1990)]
    [InlineData("Ada,03-14", "Ada", 3, 14, null)]
    [InlineData("Ada,1990/03/14", "Ada", 3, 14, 1990)]
    [InlineData("Ada,3-14", "Ada", 3, 14, null)]
    [InlineData("  Ada  ,  03-14  ", "Ada", 3, 14, null)]
    public void ReadableLines_AreRead(string line, string name, int month, int day, int? year)
    {
        var birthday = BirthdayCsv.ParseLine(line);

        Assert.NotNull(birthday);
        Assert.Equal(new Birthday(name, month, day, year), birthday);
    }

    /// <summary>
    /// What the parser must refuse. Mostly the point of the whole class: this file is typed by a
    /// person, and a rule that eats a line it should have skipped puts a birthday on the wrong day
    /// and says nothing about it.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("# a comment")]
    [InlineData("name,date")]
    [InlineData("Ada")]
    [InlineData("Ada,")]
    [InlineData(",03-14")]
    [InlineData("Ada,notadate")]
    [InlineData("Ada,2026-13-40")]
    [InlineData("Ada,14")]
    public void UnreadableLines_AreSkipped(string line)
    {
        Assert.Null(BirthdayCsv.ParseLine(line));
    }

    /// <summary>
    /// A name with a comma in it is a name people write, and a spreadsheet quotes it on the way
    /// out. Splitting on commas alone would turn "Smith, Jane" into a person called Smith with a
    /// birthday on the word Jane.
    /// </summary>
    [Fact]
    public void AQuotedName_KeepsItsComma()
    {
        var birthday = BirthdayCsv.ParseLine("\"Smith, Jane\",1990-03-14");

        Assert.Equal("Smith, Jane", birthday?.Name);
        Assert.Equal(3, birthday?.Month);
    }

    /// <summary>
    /// Day-first is not a format this reads, and that is deliberate: 03-04 means different things
    /// to different people, and guessing would be wrong silently. A four-digit year is what tells
    /// the long form from the short one, so a year can never be mistaken for a day.
    /// </summary>
    [Fact]
    public void AFourDigitYear_IsNeverReadAsADay()
    {
        Assert.Equal(new Birthday("Ada", 1, 2, 2001), BirthdayCsv.ParseLine("Ada,2001-01-02"));
    }

    [Fact]
    public void AWholeFile_SkipsWhatItCannotRead()
    {
        var text = """
            # comment
            Ada Lovelace,1815-12-10

            rubbish
            Mum,03-14
            """;

        var birthdays = BirthdayCsv.Parse(text);

        Assert.Equal(2, birthdays.Count);
        Assert.Equal("Ada Lovelace", birthdays[0].Name);
        Assert.Equal("Mum", birthdays[1].Name);
    }

    [Fact]
    public void Parse_HandlesWindowsLineEndings()
    {
        Assert.Equal(2, BirthdayCsv.Parse("Ada,03-14\r\nMum,04-01\r\n").Count);
    }

    /// <summary>
    /// A round trip, including the quoting. The written file also has to be one the parser reads
    /// back as the same list -- and must not re-add the example line the template carries, or the
    /// list would grow by one Ada Lovelace on every save.
    /// </summary>
    [Fact]
    public void Format_RoundTrips_WithoutTheExample()
    {
        List<Birthday> birthdays = [new("Smith, Jane", 3, 14, 1990), new("Mum", 4, 1, null)];

        var read = BirthdayCsv.Parse(BirthdayCsv.Format(birthdays));

        Assert.Equal(birthdays, read);
    }

    /// <summary>The file a fresh install gets has to be one the parser accepts.</summary>
    [Fact]
    public void TheTemplate_IsReadable()
    {
        Assert.Single(BirthdayCsv.Parse(BirthdayCsv.Template));
    }
}
