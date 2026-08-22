using System.Text.RegularExpressions;

namespace Dock.Core.Models;

/// <summary>
/// Decides whether a calendar event is somebody's birthday, and whose.
///
/// The whole feature rests on this being conservative. A false negative is a birthday that does not
/// appear, which the CSV is there to cover; a false positive is the island clearing the pill and
/// throwing confetti because somebody had "buy birthday present" on their calendar, and it stays
/// there until dismissed. The two are not equally bad, so the rule is deliberately narrow.
///
/// **The rule.** The title must contain a birthday word, and that word must be in one of three
/// positions:
///
/// <list type="number">
/// <item>**Possessive** -- <c>Tom's birthday</c>. The strongest signal there is, and the only one
/// that may appear anywhere in the title, so <c>Sarah's Birthday Party</c> still counts.</item>
/// <item>**Leading** -- <c>Birthday: Tom</c>, <c>Bday - Sarah</c>.</item>
/// <item>**Trailing** -- <c>Tom birthday</c>.</item>
/// </list>
///
/// A birthday word buried in the middle with no possessive is *not* a birthday, and that single
/// restriction is what rejects "buy birthday present for Sarah", "birthday card shopping" and
/// "book birthday dinner" while keeping every way anybody actually writes the event itself.
/// </summary>
public static partial class BirthdayTitle
{
    /// <summary>
    /// The spellings accepted. <c>bday</c> and <c>b-day</c> are in because they are what people
    /// type when the calendar box is small, which is the case this feature exists for.
    /// </summary>
    private const string Words = @"birthdays?|bday|b-day|b'day";

    [GeneratedRegex($@"^(?<name>.+?)['\u2019]s\s+(?:{Words})\b", RegexOptions.IgnoreCase)]
    private static partial Regex Possessive();

    /// <summary>"Birthday: Tom", "Bday - Sarah", "Birthday for Sarah" -- an explicit separator.</summary>
    [GeneratedRegex($@"^(?:{Words})\b\s*(?:[:\-\u2013\u2014]\s*|for\s+|of\s+)(?<name>.+)$",
        RegexOptions.IgnoreCase)]
    private static partial Regex LeadingWithSeparator();

    /// <summary>"Bday Tom" -- nothing but a space between the word and the name.</summary>
    [GeneratedRegex($@"^(?:{Words})\s+(?<name>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingPlain();

    [GeneratedRegex($@"^(?<name>.*?)[\s:\-\u2013\u2014]*\b(?:{Words})$", RegexOptions.IgnoreCase)]
    private static partial Regex Trailing();

    /// <summary>
    /// The bare word on its own, with nothing attached. Still a birthday -- somebody wrote it that
    /// way -- but it has no name in it to find.
    /// </summary>
    [GeneratedRegex($@"^(?:{Words})$", RegexOptions.IgnoreCase)]
    private static partial Regex BareWord();

    /// <summary>
    /// How many words a name may run to when the title is not possessive.
    ///
    /// This is the guard that rejects "Birthday card shopping trip" while keeping "Bday Tom". A
    /// leading birthday word followed by a *phrase* is somebody's errand; followed by a word or two
    /// it is a name. An explicit separator ("Birthday: ...") is itself a statement that a name
    /// follows, so it earns a little more rope -- but not unlimited, or the separator becomes a way
    /// to smuggle any sentence through.
    ///
    /// A possessive is exempt entirely, because "Mary-Anne O'Brien's birthday" is unambiguous
    /// however long the name runs.
    /// </summary>
    private const int MaxPlainNameWords = 2;

    private const int MaxSeparatedNameWords = 3;

    /// <summary>Leading decoration people put on a calendar entry -- a cake, a party popper, a star.</summary>
    [GeneratedRegex(@"^[^\p{L}\p{N}]+")]
    private static partial Regex LeadingDecoration();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>
    /// The name whose birthday this is, or null if the title is not a birthday at all.
    ///
    /// Returns the title itself when it is a bare "Birthday" with no name attached -- an event
    /// somebody wrote that way is still a birthday, and showing it unnamed beats not showing it.
    /// </summary>
    public static string? NameFrom(string? summary)
    {
        if (string.IsNullOrWhiteSpace(summary))
            return null;

        // Emoji and leading punctuation come off first, or "🎂 Tom's birthday" fails the possessive
        // pattern's anchor and falls through to being read as a leading match with the cake in the
        // name. Collapsed whitespace, because a folded feed line rejoins with runs of spaces in it.
        var title = Whitespace().Replace(summary, " ").Trim();
        title = LeadingDecoration().Replace(title, "").Trim();

        if (title.Length == 0)
            return null;

        // A possessive is the one form allowed to name anybody, however long the name runs.
        if (Possessive().Match(title) is { Success: true } possessive)
            return Clean(possessive.Groups["name"].Value) ?? title;

        if (BareWord().IsMatch(title))
            return title;

        if (LeadingWithSeparator().Match(title) is { Success: true } separated)
            return Within(separated.Groups["name"].Value, MaxSeparatedNameWords);

        if (LeadingPlain().Match(title) is { Success: true } plain)
            return Within(plain.Groups["name"].Value, MaxPlainNameWords);

        if (Trailing().Match(title) is { Success: true } trailing)
            return Within(trailing.Groups["name"].Value, MaxPlainNameWords);

        return null;
    }

    /// <summary>
    /// The name, if it is short enough to be one. Null otherwise, which makes the whole title not a
    /// birthday rather than a birthday belonging to a sentence.
    /// </summary>
    private static string? Within(string name, int maxWords)
    {
        if (Clean(name) is not { } cleaned)
            return null;

        return cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= maxWords
            ? cleaned
            : null;
    }

    /// <summary>Whether this title reads as somebody's birthday.</summary>
    public static bool IsBirthday(string? summary) => NameFrom(summary) is not null;

    /// <summary>
    /// Tidies an extracted name, or returns null when nothing usable is left.
    ///
    /// Trailing possessives and stray punctuation are trimmed because the patterns hand back
    /// whatever sat beside the birthday word, and that regularly includes the separator.
    /// </summary>
    private static string? Clean(string name)
    {
        var cleaned = name.Trim().Trim('-', ':', '\u2013', '\u2014', ',', '.', '(', ')').Trim();
        return cleaned.Length > 0 ? cleaned : null;
    }

    /// <summary>
    /// Turns a matching calendar event into a birthday, or null if it is not one.
    ///
    /// **The year is deliberately dropped.** A calendar event's DTSTART year is the year the event
    /// falls in, not the year the person was born -- so carrying it through would have the island
    /// announce that Tom is turning 2026. A calendar-sourced birthday therefore never shows an age,
    /// and the CSV stays the only place a birth year can come from. That is also what makes the two
    /// sources worth merging rather than one replacing the other.
    /// </summary>
    public static Birthday? ToBirthday(IcsEvent calendarEvent)
    {
        ArgumentNullException.ThrowIfNull(calendarEvent);

        if (NameFrom(calendarEvent.Summary) is not { } name)
            return null;

        var birthday = new Birthday(name, calendarEvent.Start.Month, calendarEvent.Start.Day, Year: null);
        return birthday.IsValid ? birthday : null;
    }
}
