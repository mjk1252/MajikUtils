using System.Globalization;
using System.Text;

namespace Dock.Core.Models;

/// <summary>One dated event out of an iCalendar feed. The whole of what this app reads from one.</summary>
/// <param name="Summary">The event's title, unescaped.</param>
/// <param name="Start">The day it starts on. The time of day is discarded -- see the parser.</param>
/// <param name="RepeatsYearly">Whether it carries a yearly RRULE.</param>
public sealed record IcsEvent(string Summary, DateOnly Start, bool RepeatsYearly);

/// <summary>
/// Reads the parts of an iCalendar (RFC 5545) feed this app cares about, and nothing else.
///
/// Deliberately not a general iCalendar implementation. A calendar feed is a large format and
/// almost none of it bears on the question being asked here, which is "what is this event called
/// and what day is it on". Timezones, attendees, alarms, exceptions, statuses and recurrence
/// arithmetic are all read straight past. That is a decision rather than an omission: a birthday
/// is an all-day event repeating yearly, and the machinery needed to place a 09:00 meeting in
/// Auckland correctly buys this feature nothing.
///
/// Two details of the format do matter and are handled properly, because getting either wrong
/// silently drops events rather than failing:
///
/// <list type="bullet">
/// <item>**Folding.** A line longer than 75 octets is split, and continued on the next line
/// beginning with a space or tab. Parsing without unfolding first truncates every long title and
/// turns the remainder into a junk property.</item>
/// <item>**Parameters.** A property name can carry parameters before its colon --
/// <c>DTSTART;VALUE=DATE:20260822</c> -- so the name is what precedes the first <c>;</c> or
/// <c>:</c>, not everything before the colon.</item>
/// </list>
/// </summary>
public static class IcsCalendar
{
    /// <summary>
    /// Every event in the feed that has both a title and a start date. Anything unreadable is
    /// skipped rather than thrown over: this is a document from another system, and one malformed
    /// VEVENT in a year of them should cost that event.
    /// </summary>
    public static List<IcsEvent> Parse(string? text)
    {
        var events = new List<IcsEvent>();
        if (string.IsNullOrWhiteSpace(text))
            return events;

        string? summary = null;
        DateOnly? start = null;
        var yearly = false;
        var inEvent = false;

        foreach (var line in Unfold(text))
        {
            if (line.Equals("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                inEvent = true;
                summary = null;
                start = null;
                yearly = false;
                continue;
            }

            if (line.Equals("END:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                if (inEvent && summary is { Length: > 0 } && start is { } date)
                    events.Add(new IcsEvent(summary, date, yearly));

                inEvent = false;
                continue;
            }

            if (!inEvent)
                continue;

            var (name, value) = SplitProperty(line);

            switch (name.ToUpperInvariant())
            {
                case "SUMMARY":
                    summary = Unescape(value);
                    break;

                // The first DTSTART wins. A VEVENT has only one, but a malformed feed repeating it
                // should not have the last copy quietly overwrite the first.
                case "DTSTART" when start is null:
                    start = ParseDate(value);
                    break;

                case "RRULE":
                    yearly = value.Contains("FREQ=YEARLY", StringComparison.OrdinalIgnoreCase);
                    break;
            }
        }

        return events;
    }

    /// <summary>
    /// Undoes RFC 5545 line folding: a line beginning with a space or tab is a continuation of the
    /// one before it, with that single leading character removed.
    /// </summary>
    private static List<string> Unfold(string text)
    {
        var unfolded = new List<string>();
        var current = new StringBuilder();

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');

            if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t'))
            {
                current.Append(line, 1, line.Length - 1);
                continue;
            }

            if (current.Length > 0)
                unfolded.Add(current.ToString());

            current.Clear();
            current.Append(line);
        }

        if (current.Length > 0)
            unfolded.Add(current.ToString());

        return unfolded;
    }

    /// <summary>
    /// Splits a content line into its property name and value.
    ///
    /// The name ends at the first <c>;</c> or <c>:</c>, whichever comes first, because parameters
    /// sit between the two. Splitting on the colon alone reads the name of
    /// <c>DTSTART;VALUE=DATE:20260822</c> as "DTSTART;VALUE=DATE", which matches nothing and drops
    /// the date -- and all-day events are the ones that are written that way, which is to say all
    /// the birthdays.
    /// </summary>
    private static (string Name, string Value) SplitProperty(string line)
    {
        var colon = line.IndexOf(':');
        if (colon < 0)
            return (line, string.Empty);

        var semicolon = line.IndexOf(';');
        var nameEnd = semicolon >= 0 && semicolon < colon ? semicolon : colon;

        return (line[..nameEnd], line[(colon + 1)..]);
    }

    /// <summary>
    /// Reads a DTSTART value as a plain date.
    ///
    /// Both forms are accepted -- <c>20260822</c> for an all-day event and <c>20260822T090000Z</c>
    /// for a timed one -- and the time is discarded either way. That is the one lossy decision in
    /// this file and it is deliberate: everything downstream asks only which day something falls
    /// on, and converting a timed event out of its own timezone would be work done solely to throw
    /// the result away. The consequence is that an event within a few hours of midnight in a
    /// distant timezone can land on the neighbouring day, which for a birthday nobody typed a time
    /// on cannot arise.
    /// </summary>
    private static DateOnly? ParseDate(string value)
    {
        var text = value.Trim();

        // A timed value carries the clock after a T. Everything from there on is discarded.
        var t = text.IndexOf('T');
        if (t > 0)
            text = text[..t];

        return DateOnly.TryParseExact(text, "yyyyMMdd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date)
            ? date
            : null;
    }

    /// <summary>
    /// Undoes the text escaping RFC 5545 applies to a value: commas, semicolons and backslashes are
    /// escaped, and <c>\n</c> stands for a newline.
    ///
    /// It matters here for exactly one reason: a title like <c>Smith\, Jane's birthday</c> comes
    /// out of a feed escaped, and a name with a stray backslash in it looks like the app is broken
    /// rather than like the feed is doing what the specification says.
    /// </summary>
    private static string Unescape(string value)
    {
        if (!value.Contains('\\'))
            return value.Trim();

        var text = new StringBuilder(value.Length);

        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != '\\' || i + 1 >= value.Length)
            {
                text.Append(value[i]);
                continue;
            }

            var next = value[++i];
            text.Append(next switch
            {
                'n' or 'N' => '\n',
                _ => next
            });
        }

        return text.ToString().Trim();
    }
}
