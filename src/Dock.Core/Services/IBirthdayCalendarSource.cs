using Dock.Core.Models;

namespace Dock.Core.Services;

/// <summary>
/// Fetches a subscribed iCalendar feed and hands back the birthdays in it.
///
/// Best-effort, like everything else here that reaches outside the machine: no network, a URL the
/// user mistyped, or a feed Google has stopped serving are all the same answer from the island's
/// point of view -- no calendar birthdays this time, and the CSV carries on alone.
/// </summary>
public interface IBirthdayCalendarSource
{
    Task<IReadOnlyList<Birthday>> GetBirthdaysAsync(string url, CancellationToken cancellationToken);
}
