using System.Globalization;
using System.Text.RegularExpressions;

namespace Dock.Core.Models;

/// <summary>
/// Reads a clock time off the front of a line: "9am", "9:30pm", "17:00", "9".
///
/// The island's countdowns have always been relative -- start twenty-five minutes and go -- which
/// is the wrong shape for the other half of what people want from a timer. "Call Tom at nine" is
/// not twenty-three minutes and forty seconds, and making somebody work that out before they can
/// type it is exactly the friction the capture box exists to remove.
/// </summary>
public static partial class ReminderTime
{
    [GeneratedRegex(@"^(?<h>\d{1,2})(?::(?<m>\d{2}))?\s*(?<suffix>am|pm)?$", RegexOptions.IgnoreCase)]
    private static partial Regex ClockPattern();

    /// <summary>
    /// Splits "9am call Tom" into the moment and the label. Fails on anything whose first word is
    /// not a time, so that a line beginning with an at-sign but not a clock -- "@home buy milk" --
    /// can fall through to being an ordinary task rather than being swallowed.
    /// </summary>
    public static bool TryParse(string? input, DateTimeOffset now, out DateTimeOffset when, out string label)
    {
        when = default;
        label = string.Empty;

        var text = (input ?? string.Empty).Trim();
        if (text.Length == 0)
            return false;

        var split = text.IndexOf(' ');
        var clock = split < 0 ? text : text[..split];
        var rest = split < 0 ? string.Empty : text[(split + 1)..].Trim();

        var match = ClockPattern().Match(clock);
        if (!match.Success)
            return false;

        var hour = int.Parse(match.Groups["h"].Value, CultureInfo.InvariantCulture);
        var minute = match.Groups["m"].Success
            ? int.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture)
            : 0;

        if (minute > 59)
            return false;

        var suffix = match.Groups["suffix"].Value.ToLowerInvariant();

        if (suffix.Length > 0)
        {
            if (hour is < 1 or > 12)
                return false;

            // 12am is midnight and 12pm is noon: the one pair the arithmetic gets wrong if you
            // just add twelve.
            hour = suffix == "am" ? hour % 12 : hour % 12 + 12;
        }
        else if (hour > 23)
        {
            return false;
        }

        var target = new DateTimeOffset(now.Year, now.Month, now.Day, hour, minute, 0, now.Offset);

        // A time that has already gone means tomorrow. Nobody types a reminder for the past, and
        // refusing one would just make them work out that they meant the next day.
        if (target <= now)
            target = target.AddDays(1);

        when = target;
        label = rest;
        return true;
    }
}
