using Dock.Core.Models;

namespace Dock.Core.Tests;

/// <summary>
/// Which calendar events count as a birthday.
///
/// The rejections are the important half. A false negative is a birthday that does not show, which
/// the CSV covers; a false positive is the island clearing the pill and throwing confetti because
/// somebody wrote "buy birthday present" on a Tuesday -- and staying that way until dismissed.
/// </summary>
public class BirthdayTitleTests
{
    [Theory]
    [InlineData("Tom's birthday", "Tom")]
    [InlineData("Tom's Birthday", "Tom")]
    [InlineData("Tom's bday", "Tom")]
    [InlineData("Tom's b-day", "Tom")]
    [InlineData("Sarah's Birthday Party", "Sarah")]
    [InlineData("Smith, Jane's birthday", "Smith, Jane")]
    [InlineData("Mary-Anne O'Brien's birthday", "Mary-Anne O'Brien")]
    public void APossessive_NamesThePerson(string summary, string expected)
    {
        Assert.Equal(expected, BirthdayTitle.NameFrom(summary));
    }

    [Theory]
    [InlineData("Birthday: Tom", "Tom")]
    [InlineData("Birthday - Sarah", "Sarah")]
    [InlineData("Bday Tom", "Tom")]
    [InlineData("Birthday for Sarah", "Sarah")]
    public void ALeadingBirthdayWord_NamesWhatFollows(string summary, string expected)
    {
        Assert.Equal(expected, BirthdayTitle.NameFrom(summary));
    }

    [Theory]
    [InlineData("Tom birthday", "Tom")]
    [InlineData("Sarah Bday", "Sarah")]
    public void ATrailingBirthdayWord_NamesWhatPrecedes(string summary, string expected)
    {
        Assert.Equal(expected, BirthdayTitle.NameFrom(summary));
    }

    /// <summary>
    /// The whole point of the position rule. A birthday word in the middle of a sentence, with no
    /// possessive anywhere, is somebody's errand rather than somebody's birthday.
    /// </summary>
    [Theory]
    [InlineData("Buy birthday present for Sarah")]
    [InlineData("Book birthday dinner at the Italian place")]
    public void ABirthdayWordInTheMiddle_IsNotABirthday(string summary)
    {
        Assert.False(BirthdayTitle.IsBirthday(summary));
    }

    /// <summary>
    /// The word cap, and the case it was added for. A leading birthday word followed by a *phrase*
    /// is somebody's errand; followed by a word or two it is a name. Without this, "Birthday card
    /// shopping trip" put a person called "card shopping trip" on the island for the day.
    /// </summary>
    [Theory]
    [InlineData("Birthday card shopping trip")]
    [InlineData("Birthday presents to wrap tonight")]
    [InlineData("Bday cake ingredients from the shop")]
    public void ALeadingBirthdayWordFollowedByAPhrase_IsNotABirthday(string summary)
    {
        Assert.False(BirthdayTitle.IsBirthday(summary));
    }

    /// <summary>But a short name after the same leading word still counts.</summary>
    [Theory]
    [InlineData("Bday Tom", "Tom")]
    [InlineData("Birthday Sarah Smith", "Sarah Smith")]
    [InlineData("Birthday: Mary Jane Watson", "Mary Jane Watson")]
    public void ALeadingBirthdayWordFollowedByAName_Counts(string summary, string expected)
    {
        Assert.Equal(expected, BirthdayTitle.NameFrom(summary));
    }

    [Theory]
    [InlineData("Standup")]
    [InlineData("Dentist")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void OrdinaryEvents_AreNotBirthdays(string? summary)
    {
        Assert.False(BirthdayTitle.IsBirthday(summary));
    }

    /// <summary>Decoration people put on a calendar entry comes off before the name is read.</summary>
    [Fact]
    public void LeadingEmoji_IsStripped()
    {
        Assert.Equal("Tom", BirthdayTitle.NameFrom("\U0001F382 Tom's birthday"));
    }

    /// <summary>A bare "Birthday" is still a birthday. Unnamed beats not shown.</summary>
    [Fact]
    public void ABareBirthdayWord_IsStillABirthday()
    {
        Assert.Equal("Birthday", BirthdayTitle.NameFrom("Birthday"));
    }

    /// <summary>
    /// The year is dropped on purpose. A calendar event's DTSTART year is the year the event falls
    /// in, not the year the person was born, so keeping it would have the island announce that Tom
    /// is turning 2026.
    /// </summary>
    [Fact]
    public void ACalendarBirthday_NeverCarriesAnAge()
    {
        var birthday = BirthdayTitle.ToBirthday(
            new IcsEvent("Tom's birthday", new DateOnly(2026, 8, 22), RepeatsYearly: true));

        Assert.NotNull(birthday);
        Assert.Equal("Tom", birthday.Name);
        Assert.Equal(8, birthday.Month);
        Assert.Equal(22, birthday.Day);
        Assert.Null(birthday.Year);
        Assert.Null(birthday.TurningAge(new DateOnly(2026, 8, 22)));
    }

    [Fact]
    public void ANonBirthdayEvent_ConvertsToNothing()
    {
        Assert.Null(BirthdayTitle.ToBirthday(
            new IcsEvent("Standup", new DateOnly(2026, 8, 22), RepeatsYearly: false)));
    }
}
