using System.Text.RegularExpressions;
using Dock.Core.Services;

namespace Dock.Core.Models;

/// <summary>
/// Reads a badge off what a taskbar button tells an accessibility client about itself.
///
/// The taskbar has no API for this. What it has is an accessible name and a help text per button,
/// built for screen readers, and between them they spell out everything the button is showing.
/// The badge is in the <em>help text</em> -- which cost a release to find out, because the name is
/// the obvious place to look and the name never mentions it:
///
/// <code>
/// help ''                                    nothing waiting
/// help '0 notifications'                     nothing waiting, said out loud
/// help '3 notifications'                     a badge of three
/// help 'Unread messages'                     a badge with no number on it
/// help 'Attention requested, 0 notifications'  the same, and the count channel saying zero
/// </code>
///
/// That last one is the shape that matters most, because it is two independent facts joined by a
/// comma and reading it as one throws the badge away. "Attention requested" is the dot. "0
/// notifications" is the *numeric* channel reporting that it has no number to give. A parser that
/// takes the count as authoritative concludes there is no badge, while the button is sitting there
/// with a dot on it.
///
/// So a count wins only when it is a count of something. Failing that, any wording about attention
/// or unread things is a badge with no number, which is what a dot looks like from here -- and an
/// app that only ever speaks that way, as Discord does, would otherwise be invisible.
///
/// So the parsing lives here, in a project with no taskbar in it, where every shape the strings
/// come in can be asserted directly. The half that needs a running explorer is the walk that
/// fetches them, and that is all it does.
///
/// Written to fail quietly rather than cleverly. These are localised strings from a component
/// nobody promised would keep saying the same thing, so text that matches nothing here is a button
/// with no badge -- never an exception, and never a guess.
/// </summary>
public static class TaskbarButtonName
{
    /// <summary>How the taskbar prefixes the AppUserModelID it puts in a button's automation id.</summary>
    private const string AppIdPrefix = "Appid: ";

    /// <summary>
    /// A number of things waiting.
    ///
    /// The count has to be bound to the word rather than merely present in the same string, and
    /// that is not a detail: "File Pilot - 1 running window pinned" contains a digit, and a pattern
    /// that took any digit it found would report every ordinary running app on the taskbar as
    /// carrying a badge of one.
    ///
    /// <c>9+</c> is matched by its leading digits -- an app with more than ninety-nine unread is
    /// not going to be misread by rounding.
    /// </summary>
    private static readonly Regex CountPattern = new(
        @"(\d+)\+?\s+(?:new\s+)?(?:notification|unread|message)s?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Wording that means something is waiting without saying how much.
    ///
    /// Deliberately stems rather than exact strings: the shell says "Unread messages" for one app,
    /// "Attention requested" for the same app a minute later, and the next build will say something
    /// else again. Notice that bare "notification" is *not* here -- it appears in "0
    /// notifications", which is a button saying it has nothing. "New notification" is, since that
    /// is an arrival with the number left off, and the lookbehind is what keeps "No new
    /// notifications" from reading as one.
    /// </summary>
    private static readonly Regex DotPattern = new(
        @"attention|unread|new\s+message|(?<!no\s)new\s+notification",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// The badge on one taskbar button, or null if it has none.
    /// </summary>
    /// <param name="name">The button's accessible name, which carries the app's display name.</param>
    /// <param name="automationId">The button's automation id, which carries the AppUserModelID.
    /// Anything not in that form is not an app button -- it is Start, or Widgets, or a tray
    /// icon -- and is rejected here rather than by the caller.</param>
    /// <param name="helpText">The button's help text, which is where the badge actually is.</param>
    public static TaskbarBadge? ReadBadge(string? name, string? automationId, string? helpText)
    {
        if (string.IsNullOrWhiteSpace(name) ||
            automationId is null ||
            !automationId.StartsWith(AppIdPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        // Help text first, since that is where the shell actually puts it. The name is a fallback
        // for any app or Windows build that says it there instead -- it costs one regex against a
        // string already in hand, and the alternative is missing a badge outright.
        if ((ReadBadgeCount(helpText) ?? ReadBadgeCount(name)) is not { } count)
            return null;

        var appId = automationId[AppIdPrefix.Length..].Trim();
        if (appId.Length == 0)
            return null;

        return new TaskbarBadge(appId, ReadAppName(name), count);
    }

    /// <summary>
    /// The notification centre's own total, off the clock-and-notifications button at the end of
    /// the tray: <c>Notifications 5 new notifications (Do not disturb on)</c>.
    ///
    /// Zero when there is nothing waiting, which is also what an unrecognised string gives -- and
    /// what an absent button gives, since the shell folds the notifications button into the clock
    /// entirely when the centre is empty. There is nothing to detect in that case and nothing that
    /// needs detecting.
    /// </summary>
    public static int ReadNotificationCentreCount(string? name) =>
        name is not null && name.StartsWith("Notification", StringComparison.OrdinalIgnoreCase)
            ? ReadBadgeCount(name) ?? 0
            : 0;

    /// <summary>
    /// What a badge string means. Null for no badge at all, zero for a badge carrying no number,
    /// and otherwise the number on it.
    ///
    /// The distinction between null and zero is the whole of this method. <c>0 notifications</c>
    /// is a button reporting that it has nothing and must read as null; <c>Unread messages</c> is
    /// a button with a dot on it and must read as zero, because something *is* waiting and only
    /// the number is missing.
    /// </summary>
    private static int? ReadBadgeCount(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        // Every match rather than the first, because the tray's own button says the word twice:
        // "Notifications 5 new notifications". A real count anywhere in the string wins.
        foreach (Match match in CountPattern.Matches(text))
        {
            if (int.TryParse(match.Groups[1].Value, out var count) && count > 0)
                return count;
        }

        // No number, or a number that turned out to be zero. Either way the count channel has
        // nothing to say, and whether there is a badge at all now rests entirely on the wording --
        // which is exactly the "Attention requested, 0 notifications" case.
        return DotPattern.IsMatch(text) ? 0 : null;
    }

    /// <summary>
    /// The app's name, which is everything up to the first dash-separated clause. Falling back to
    /// trimming "pinned" off the end covers the button that has nothing else to say about itself.
    /// </summary>
    private static string ReadAppName(string name)
    {
        var dash = name.IndexOf(" - ", StringComparison.Ordinal);
        if (dash > 0)
            return name[..dash].Trim();

        const string pinned = " pinned";

        return (name.EndsWith(pinned, StringComparison.OrdinalIgnoreCase)
            ? name[..^pinned.Length]
            : name).Trim();
    }
}
