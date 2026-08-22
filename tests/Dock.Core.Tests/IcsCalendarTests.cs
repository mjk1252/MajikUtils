using Dock.Core.Models;

namespace Dock.Core.Tests;

/// <summary>
/// The iCalendar reader, and the two details of the format that silently drop events when they are
/// got wrong -- folded lines and property parameters.
/// </summary>
public class IcsCalendarTests
{
    private const string OneEvent =
        """
        BEGIN:VCALENDAR
        VERSION:2.0
        BEGIN:VEVENT
        UID:abc123
        SUMMARY:Tom's birthday
        DTSTART;VALUE=DATE:20260822
        DTEND;VALUE=DATE:20260823
        RRULE:FREQ=YEARLY
        END:VEVENT
        END:VCALENDAR
        """;

    [Fact]
    public void AnAllDayEvent_IsRead()
    {
        var events = IcsCalendar.Parse(OneEvent);

        var read = Assert.Single(events);
        Assert.Equal("Tom's birthday", read.Summary);
        Assert.Equal(new DateOnly(2026, 8, 22), read.Start);
        Assert.True(read.RepeatsYearly);
    }

    /// <summary>
    /// The parameter case, and the reason SplitProperty exists. Splitting on the colon alone reads
    /// this property's name as "DTSTART;VALUE=DATE", which matches nothing -- and all-day events
    /// are written that way, which is to say all the birthdays.
    /// </summary>
    [Fact]
    public void APropertyWithParameters_StillHasItsName()
    {
        Assert.Single(IcsCalendar.Parse(
            "BEGIN:VEVENT\nSUMMARY:Tom's birthday\nDTSTART;VALUE=DATE:20260822\nEND:VEVENT"));
    }

    /// <summary>
    /// Folding: a long line continues on the next one, beginning with a space. Parsing without
    /// unfolding truncates every long title and turns the rest into a junk property.
    /// </summary>
    [Fact]
    public void AFoldedLine_IsRejoined()
    {
        var events = IcsCalendar.Parse(
            "BEGIN:VEVENT\nSUMMARY:Tom Andersen-Whitfield's\n  birthday\nDTSTART;VALUE=DATE:20260822\nEND:VEVENT");

        Assert.Equal("Tom Andersen-Whitfield's birthday", Assert.Single(events).Summary);
    }

    /// <summary>A timed event keeps its day and loses its clock, which is all this app asks for.</summary>
    [Fact]
    public void ATimedEvent_KeepsOnlyItsDay()
    {
        var events = IcsCalendar.Parse(
            "BEGIN:VEVENT\nSUMMARY:Standup\nDTSTART;TZID=Europe/Berlin:20260822T090000\nEND:VEVENT");

        Assert.Equal(new DateOnly(2026, 8, 22), Assert.Single(events).Start);
    }

    [Fact]
    public void EscapedText_IsUnescaped()
    {
        var events = IcsCalendar.Parse(
            "BEGIN:VEVENT\nSUMMARY:Smith\\, Jane's birthday\nDTSTART;VALUE=DATE:20260822\nEND:VEVENT");

        Assert.Equal("Smith, Jane's birthday", Assert.Single(events).Summary);
    }

    [Fact]
    public void WindowsLineEndings_AreHandled()
    {
        Assert.Single(IcsCalendar.Parse(
            "BEGIN:VEVENT\r\nSUMMARY:Tom's birthday\r\nDTSTART;VALUE=DATE:20260822\r\nEND:VEVENT\r\n"));
    }

    /// <summary>An event missing either half of what is needed is skipped, not guessed at.</summary>
    [Theory]
    [InlineData("BEGIN:VEVENT\nDTSTART;VALUE=DATE:20260822\nEND:VEVENT")]
    [InlineData("BEGIN:VEVENT\nSUMMARY:No date here\nEND:VEVENT")]
    [InlineData("BEGIN:VEVENT\nSUMMARY:Bad date\nDTSTART;VALUE=DATE:notadate\nEND:VEVENT")]
    public void IncompleteEvents_AreSkipped(string ics)
    {
        Assert.Empty(IcsCalendar.Parse(ics));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("not a calendar at all")]
    public void Rubbish_IsAnEmptyList(string? text)
    {
        Assert.Empty(IcsCalendar.Parse(text));
    }

    /// <summary>Properties outside a VEVENT belong to the calendar, not to an event.</summary>
    [Fact]
    public void CalendarLevelProperties_AreNotEvents()
    {
        var events = IcsCalendar.Parse(
            "BEGIN:VCALENDAR\nSUMMARY:My calendar\nBEGIN:VEVENT\nSUMMARY:Tom's birthday\n" +
            "DTSTART;VALUE=DATE:20260822\nEND:VEVENT\nEND:VCALENDAR");

        Assert.Equal("Tom's birthday", Assert.Single(events).Summary);
    }

    [Fact]
    public void SeveralEvents_AreAllRead()
    {
        var ics = string.Join('\n',
            "BEGIN:VCALENDAR",
            "BEGIN:VEVENT", "SUMMARY:Tom's birthday", "DTSTART;VALUE=DATE:20260822", "END:VEVENT",
            "BEGIN:VEVENT", "SUMMARY:Standup", "DTSTART;VALUE=DATE:20260823", "END:VEVENT",
            "END:VCALENDAR");

        Assert.Equal(2, IcsCalendar.Parse(ics).Count);
    }

    /// <summary>
    /// State from one event must not leak into the next. A VEVENT with no RRULE following one that
    /// had a yearly rule should not inherit it.
    /// </summary>
    [Fact]
    public void EachEventStartsClean()
    {
        var ics = string.Join('\n',
            "BEGIN:VEVENT", "SUMMARY:Tom's birthday", "DTSTART;VALUE=DATE:20260822",
            "RRULE:FREQ=YEARLY", "END:VEVENT",
            "BEGIN:VEVENT", "SUMMARY:Standup", "DTSTART;VALUE=DATE:20260823", "END:VEVENT");

        var events = IcsCalendar.Parse(ics);

        Assert.True(events[0].RepeatsYearly);
        Assert.False(events[1].RepeatsYearly);
    }
}
