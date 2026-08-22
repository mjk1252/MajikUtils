using System.Globalization;
using System.Text;

namespace Dock.Core.Models;

/// <summary>
/// Reads and writes the birthday list's file format, and nothing else -- no file IO, so the awkward
/// half of this feature can be asserted without a disk.
///
/// The format is a CSV because the list is meant to be kept by hand, in whatever the user already
/// opens .csv files with, and because a birthday list is genuinely two columns. That it opens in a
/// spreadsheet is the point of choosing it over JSON: nobody maintains a list of their friends'
/// birthdays in a text editor with braces in it.
///
/// Parsing is deliberately forgiving in every direction that cannot be ambiguous. A line that makes
/// no sense is skipped rather than thrown over: this file is typed by a person, and one mistyped row
/// costing the other forty is the wrong trade for something whose entire job is to be edited.
/// </summary>
public static class BirthdayCsv
{
    /// <summary>
    /// The comment block every written file starts with.
    ///
    /// Both accepted date shapes appear in it, which is the only documentation of the format
    /// anybody will actually read -- a header comment in the file being edited beats a paragraph in
    /// a settings window nobody opened.
    /// </summary>
    public const string Header =
        """
        # MajikUtils birthdays. One person per line: name,date
        #
        # The date is 1990-03-14 when you know the year, or 03-14 when you would rather
        # not say -- the year only decides whether an age is shown. Lines starting with a
        # # are ignored, and so are blank ones. Save the file and the island picks it up.
        #
        # name,date

        """;

    /// <summary>
    /// What a file that does not exist yet is created with: the header, and one line showing what
    /// a filled-in one looks like.
    ///
    /// Kept apart from <see cref="Header"/> because <see cref="Format"/> must not emit the example
    /// -- a rewrite that re-added Ada Lovelace every time would grow the list on its own.
    /// </summary>
    public const string Template = Header + "Ada Lovelace,1815-12-10\n";

    /// <summary>
    /// Every birthday in the text, in the order they were written, skipping anything unreadable.
    /// </summary>
    public static List<Birthday> Parse(string? text)
    {
        var birthdays = new List<Birthday>();
        if (string.IsNullOrWhiteSpace(text))
            return birthdays;

        foreach (var line in text.Split('\n'))
        {
            if (ParseLine(line) is { } birthday)
                birthdays.Add(birthday);
        }

        return birthdays;
    }

    /// <summary>
    /// One line, or null if it is a comment, blank, a header row, or simply wrong.
    ///
    /// Public rather than folded into <see cref="Parse"/> so the tests can name the cases one at a
    /// time. What they are mostly proving is which lines are *not* birthdays, and a table of forty
    /// rejected lines asserted through the whole-file path is a count rather than an explanation.
    /// </summary>
    public static Birthday? ParseLine(string line)
    {
        var trimmed = line.Trim().TrimEnd('\r');

        if (trimmed.Length == 0 || trimmed[0] is '#' or ';')
            return null;

        var fields = SplitFields(trimmed);
        if (fields.Count < 2)
            return null;

        var name = fields[0].Trim();
        var date = fields[1].Trim();

        if (name.Length == 0 || date.Length == 0)
            return null;

        // A spreadsheet writes one of these out when it saves, so a round trip through Excel does
        // not reappear as a person called "name" with a birthday on the word "date".
        if (name.Equals("name", StringComparison.OrdinalIgnoreCase))
            return null;

        if (ParseDate(date) is not { } parsed)
            return null;

        var birthday = new Birthday(name, parsed.Month, parsed.Day, parsed.Year);
        return birthday.IsValid ? birthday : null;
    }

    /// <summary>
    /// The date column. Two shapes, and only two: <c>yyyy-MM-dd</c> and <c>MM-dd</c>, with a slash
    /// or a dot accepted wherever a dash is.
    ///
    /// Nothing ambiguous is accepted, and that is the rule the whole method exists to keep. "03-04"
    /// is March the 4th here and April the 3rd to half the world, so day-first input is simply not
    /// a format this reads -- guessing would put a birthday a month out and say nothing about it,
    /// which is worse than the line being skipped. A four-digit year is what identifies the longer
    /// shape, so a stray "2001" can never be read as a day.
    /// </summary>
    private static (int Month, int Day, int? Year)? ParseDate(string value)
    {
        var parts = value.Split(['-', '/', '.'], StringSplitOptions.RemoveEmptyEntries);

        static bool Number(string text, out int result) =>
            int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out result);

        switch (parts.Length)
        {
            case 2 when Number(parts[0], out var month) && Number(parts[1], out var day):
                return (month, day, null);

            case 3 when parts[0].Length == 4 && Number(parts[0], out var year) &&
                        Number(parts[1], out var m) && Number(parts[2], out var d):
                return (m, d, year);

            default:
                return null;
        }
    }

    /// <summary>
    /// Splits a CSV line, honouring double quotes around a field that contains a comma.
    ///
    /// Written out rather than done with Split(','), because the one field a person types freely is
    /// the name and "Smith, Jane" is a name people write. Doubled quotes inside a quoted field are
    /// an escaped quote, which is what every spreadsheet emits.
    /// </summary>
    private static List<string> SplitFields(string line)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (quoted)
            {
                if (c != '"')
                    field.Append(c);
                else if (i + 1 < line.Length && line[i + 1] == '"')
                    field.Append(line[++i]);
                else
                    quoted = false;
            }
            else if (c == '"')
                quoted = true;
            else if (c == ',')
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else
                field.Append(c);
        }

        fields.Add(field.ToString());
        return fields;
    }

    /// <summary>
    /// Writes birthdays back out, header comment and all.
    ///
    /// Only used by the one path that adds a person from inside the app. Hand edits are never
    /// rewritten -- the file belongs to whoever is maintaining it, and reformatting somebody's list
    /// because they opened a panel is not something an app gets to do.
    /// </summary>
    public static string Format(IEnumerable<Birthday> birthdays)
    {
        var text = new StringBuilder(Header);

        foreach (var birthday in birthdays)
        {
            var date = birthday.Year is { } year
                ? $"{year:D4}-{birthday.Month:D2}-{birthday.Day:D2}"
                : $"{birthday.Month:D2}-{birthday.Day:D2}";

            text.Append(Quote(birthday.Name)).Append(',').Append(date).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>Quotes a name only when it needs it, so an ordinary list stays plain to read.</summary>
    private static string Quote(string name) =>
        name.Contains(',') || name.Contains('"')
            ? $"\"{name.Replace("\"", "\"\"")}\""
            : name;
}
