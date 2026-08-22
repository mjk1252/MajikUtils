using System.Net.Http;
using Dock.Core.Models;
using Dock.Core.Services;

namespace Dock.Interop.Shell;

/// <summary>
/// Reads birthdays out of a subscribed iCalendar feed -- in practice a Google Calendar "secret
/// address in iCal format", though nothing here is Google-specific and any .ics URL works.
///
/// The second network call in the app, after the lyrics lookup, and built the same way: one GET, a
/// pure parser, and every failure treated as "nothing this time" rather than as something to report.
///
/// **What this cannot see.** Google's automatic *Birthdays* calendar -- the one generated from
/// Contacts -- has no secret iCal address, so it is not reachable this way at all. What is reachable
/// is birthday events on the user's own calendars, which is what the title matching exists for.
/// Reaching the generated one needs the Calendar API and an OAuth flow, which for an application
/// distributed on GitHub means Google verification for a sensitive scope; that trade was considered
/// and declined.
/// </summary>
public sealed class IcsCalendarSource : IBirthdayCalendarSource
{
    private static readonly HttpClient Http = BuildClient();

    /// <summary>
    /// A calendar feed is a text file that can run to megabytes on a busy account, and this only
    /// ever wants the few lines of it that are birthdays. The cap is what stops a pathological or
    /// hostile feed from being read into memory in full.
    /// </summary>
    private const int MaxBytes = 8 * 1024 * 1024;

    public async Task<IReadOnlyList<Birthday>> GetBirthdaysAsync(
        string url, CancellationToken cancellationToken)
    {
        if (!IsUsable(url))
            return [];

        try
        {
            using var response = await Http
                .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return [];

            if (response.Content.Headers.ContentLength is > MaxBytes)
                return [];

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (body.Length > MaxBytes)
                return [];

            return IcsCalendar.Parse(body)
                .Select(BirthdayTitle.ToBirthday)
                .OfType<Birthday>()
                .ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                       or InvalidOperationException or UriFormatException)
        {
            // Offline, a bad URL, a feed that has been revoked -- one answer covers all of them,
            // and the CSV is still the list.
            return [];
        }
    }

    /// <summary>
    /// Whether this is a URL worth trying at all.
    ///
    /// HTTPS only, and that is not tidiness: the calendar URL is a bearer credential -- anyone
    /// holding it can read the calendar -- so sending it over plain HTTP would hand it to the
    /// network. A feed offered over http:// is refused rather than downgraded to.
    /// </summary>
    public static bool IsUsable(string? url) =>
        !string.IsNullOrWhiteSpace(url) &&
        Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps;

    private static HttpClient BuildClient()
    {
        var client = new HttpClient
        {
            // Long enough for a large calendar on a slow line, short enough that a hung request is
            // not still holding a slot when the next refresh comes round.
            Timeout = TimeSpan.FromSeconds(30)
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd("MajikUtils");
        return client;
    }
}
