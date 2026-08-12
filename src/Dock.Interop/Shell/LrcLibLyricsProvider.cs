using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dock.Core.Models;
using Dock.Core.Services;

namespace Dock.Interop.Shell;

/// <summary>
/// Time-synced lyrics from lrclib.net -- a free, keyless lookup built for exactly this, which is
/// why it is the one network call anywhere in Dock that reaches somewhere other than
/// Microsoft's own services or winget.
///
/// A track is looked up once by exact artist/title/duration; nothing here retries with fuzzier
/// matching or falls back to plain (unsynced) lyrics, because a wrong match scrolling under the
/// wrong song is worse than the pill simply not offering any.
/// </summary>
public sealed partial class LrcLibLyricsProvider : ILyricsProvider
{
    private static readonly HttpClient Http = BuildClient();

    public async Task<IReadOnlyList<LyricLine>?> GetLyricsAsync(
        string artist, string title, TimeSpan duration, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(title))
            return null;

        try
        {
            var url = "https://lrclib.net/api/get" +
                       $"?artist_name={Uri.EscapeDataString(artist)}" +
                       $"&track_name={Uri.EscapeDataString(title)}" +
                       $"&duration={(int)duration.TotalSeconds}";

            using var response = await Http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(body);

            if (!document.RootElement.TryGetProperty("syncedLyrics", out var syncedElement) ||
                syncedElement.GetString() is not { Length: > 0 } synced)
            {
                return null;
            }

            var lines = Parse(synced);
            return lines.Count > 0 ? lines : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // No network, the service is unreachable, or the response was not what was expected --
            // all the same answer from here: no lyrics this time.
            return null;
        }
    }

    /// <summary>
    /// LRC's own format: one <c>[mm:ss.xx]</c> timestamp opening each line, the words after it.
    /// Lines that do not parse (a metadata tag, a blank separator) are simply skipped rather than
    /// failing the whole track.
    /// </summary>
    private static List<LyricLine> Parse(string lrc)
    {
        var result = new List<LyricLine>();

        foreach (var rawLine in lrc.Split('\n'))
        {
            var match = TimestampPattern().Match(rawLine);
            if (!match.Success)
                continue;

            var minutes = int.Parse(match.Groups[1].Value);
            var seconds = double.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
            var text = match.Groups[3].Value.Trim();

            if (text.Length == 0)
                continue;

            result.Add(new LyricLine(TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds), text));
        }

        return result;
    }

    private static HttpClient BuildClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };

        // lrclib.net asks callers to identify themselves rather than arrive as an anonymous
        // client; a repo URL is what its own documentation suggests putting there.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MajikUtils/1.0 (+https://github.com)");

        return client;
    }

    [GeneratedRegex(@"^\[(\d+):(\d+(?:\.\d+)?)\](.*)$")]
    private static partial Regex TimestampPattern();
}
