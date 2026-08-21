using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Dock.Core.ViewModels;

/// <summary>
/// The time, on the collapsed pill.
///
/// Deliberately not an <see cref="IIslandActivity"/>. Activities compete for the pill and take
/// turns holding it; a clock has to be readable while something else is playing, and one that
/// could lose its turn would be a clock that is missing exactly when it is wanted. So it is
/// chrome: it sits beside whatever holds the pill rather than in the queue for it.
///
/// The reason it exists at all is the taskbar. Auto-hiding the taskbar takes the clock with it,
/// and the island is already the thing hanging off the top edge -- so the island is where the
/// clock goes back.
/// </summary>
public sealed partial class ClockViewModel : ObservableObject
{
    /// <summary>
    /// Whether the clock is on the island at all. Off, the pill is exactly what it was before this
    /// existed: the column collapses to nothing and no width is taken from the activity.
    /// </summary>
    [ObservableProperty] private bool _isEnabled = true;

    [ObservableProperty] private string _timeText = string.Empty;

    /// <summary>Weekday and date, for the expanded panel. Never on the collapsed pill -- there is
    /// no room, and the time is the part anyone is actually looking for.</summary>
    [ObservableProperty] private string _dateText = string.Empty;

    /// <summary>What the strings were last built from, so a tick that changes neither does nothing.</summary>
    private DateTime _shown = DateTime.MinValue;

    /// <summary>
    /// Advances the clock. Called off the island's one 250ms timer rather than owning a timer of
    /// its own, and cheap on the ticks that do not matter: the text is rebuilt on the minute, not
    /// four times a second.
    /// </summary>
    /// <param name="nowLocal">Local time. Passed in rather than read here so a test can drive it.</param>
    public void Tick(DateTime nowLocal)
    {
        var minute = new DateTime(
            nowLocal.Year, nowLocal.Month, nowLocal.Day, nowLocal.Hour, nowLocal.Minute, 0);

        if (minute == _shown)
            return;

        _shown = minute;

        // The user's own short-time pattern, so 24-hour and 12-hour clocks both come out the way
        // the rest of Windows shows them. A setting of our own here would be a second answer to a
        // question the system has already asked.
        var culture = CultureInfo.CurrentCulture;
        TimeText = nowLocal.ToString(culture.DateTimeFormat.ShortTimePattern, culture);
        DateText = nowLocal.ToString("ddd d MMM", culture);
    }
}
