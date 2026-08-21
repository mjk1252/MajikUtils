using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Dock.Core.Services;

namespace Dock.Core.ViewModels;

/// <summary>
/// One number on the collapsed pill: how much has arrived since you last looked.
///
/// Chrome rather than an <see cref="IIslandActivity"/>, and for the same reason as
/// <see cref="ClockViewModel"/> beside it. Activities take turns holding the pill, and a count that
/// could lose its turn to a track change would be missing at the moment it is wanted. It stands
/// next to the clock in the collapsed layer, which is the whole point: with the taskbar hidden,
/// that strip is where the time and the waiting-things both have to be.
///
/// What shows is the live total: everything the taskbar says is waiting, summed across every badged
/// app and the notification centre. Nothing when nothing is waiting, one when one thing is, two
/// when two are. Reading the thing itself takes it back down, because the badge it was counting
/// goes away.
///
/// It counted *arrivals against a baseline* for two releases, and that was wrong. The idea was to
/// spare anyone permanently sitting on three unread mails a permanent three on the island. What it
/// actually bought was a number that could silently be zero while things were genuinely waiting,
/// because badge semantics are not consistent enough to difference reliably: Discord reports
/// "Attention requested, 0 notifications" for a dot with no count, which has to floor to one thing
/// waiting, and then reports "1 notifications" for a real one -- the same total, so a real arrival
/// produced no change at all.
///
/// A count that is occasionally wrong in the direction of *missing something* is the one failure
/// this must not have. The taskbar showed the standing total; this stands in for the taskbar.
/// </summary>
public sealed partial class BadgeCountViewModel : ObservableObject
{
    /// <summary>
    /// How many app chips the collapsed pill draws before giving up and counting the rest.
    ///
    /// Three, because the pill is a strip and the album art, the title and the clock are all
    /// entitled to their share of it. A fourth badged app is rare enough that "+1" is a fair
    /// summary and common enough that it has to say something rather than silently drop it.
    /// </summary>
    public const int MaxChips = 3;

    private readonly IIconProvider? _icons;

    /// <summary>
    /// Icons by AppUserModelID, for the life of the app. Fetching one is a shell call, the poll
    /// runs every two seconds, and an app's icon does not change -- so it is fetched once and the
    /// misses are cached too, since an id the Applications folder cannot resolve this minute will
    /// not resolve next minute either.
    /// </summary>
    private readonly Dictionary<string, byte[]?> _iconCache = new(StringComparer.OrdinalIgnoreCase);

    public BadgeCountViewModel(IIconProvider? icons = null) => _icons = icons;

    /// <summary>
    /// How long a new arrival reads as new. Purely visual -- the count is the count either way --
    /// but it is what makes something arriving look different from something merely still waiting.
    /// </summary>
    public static readonly TimeSpan Highlight = TimeSpan.FromSeconds(4);

    /// <summary>
    /// Whether the count is on the island at all. Off, the pill is exactly what it was before this
    /// existed: nothing is drawn and no width is taken from whatever holds it.
    /// </summary>
    [ObservableProperty] private bool _isEnabled = true;

    /// <summary>How many things are waiting, right now, across everything the taskbar knows of.</summary>
    [ObservableProperty] private int _count;

    /// <summary>Whether there is anything to draw. <see cref="Count"/> above zero, and enabled.</summary>
    [ObservableProperty] private bool _hasCount;

    /// <summary>
    /// True for a few seconds after the count goes up, so the template can say "this just
    /// happened" rather than "this is still true" -- the difference between a number appearing and
    /// a number sitting there.
    /// </summary>
    [ObservableProperty] private bool _isNew;

    /// <summary>
    /// Everything currently waiting, broken down: <c>Outlook 3 · Discord 1</c>. The tooltip, and
    /// the row in the expanded panel -- the places with room for the whole answer, and where the
    /// *absolute* counts belong, since somebody who has stopped to look is asking what is waiting
    /// rather than what has just changed.
    /// </summary>
    [ObservableProperty] private string _summary = string.Empty;

    /// <summary>
    /// The last reading from each of the two sources, kept apart so either can arrive on its own
    /// without wiping what the other last said. They are merged at every apply.
    /// </summary>
    private TaskbarBadgeSnapshot _taskbar = TaskbarBadgeSnapshot.Empty;
    private IReadOnlyList<AppNotifications> _centre = [];
    private IReadOnlyList<AttentionRequest> _attention = [];

    /// <summary>When the arrival stops reading as new. Null whenever it already has.</summary>
    private DateTimeOffset? _highlightUntil;

    /// <summary>
    /// How many badged apps did not fit as chips. Zero whenever they all did.
    /// </summary>
    [ObservableProperty] private int _overflow;

    /// <summary>Whether there is an overflow worth drawing.</summary>
    [ObservableProperty] private bool _hasOverflow;

    /// <summary>
    /// One chip per badged app, capped at <see cref="MaxChips"/>, loudest first -- so the app with
    /// the most waiting is the one that keeps its place when there is not room for everybody.
    /// </summary>
    public ObservableCollection<BadgeItemViewModel> Badges { get; } = [];

    /// <summary>Takes a reading from the taskbar's badges.</summary>
    public void Apply(TaskbarBadgeSnapshot snapshot, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        _taskbar = snapshot;
        Rebuild(now);
    }

    /// <summary>Takes a reading from Windows' notification centre.</summary>
    public void Apply(IReadOnlyList<AppNotifications> notifications, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(notifications);

        _centre = notifications;
        Rebuild(now);
    }

    /// <summary>
    /// Takes a reading from the windows currently asking for attention.
    ///
    /// Narrower than the other two and worth having anyway: an application that draws its own
    /// notifications raises no toast and may badge nothing, but it still flashes -- and a flash is
    /// a window-manager event, so it arrives with the taskbar hidden. It says *that* something
    /// wants you and never how many, which is all Windows knows.
    /// </summary>
    public void Apply(IReadOnlyList<AttentionRequest> attention, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(attention);

        _attention = attention;
        Rebuild(now);
    }

    /// <summary>
    /// Merges the three sources into one set of chips.
    ///
    /// Highest count per app rather than the sum, because the two frequently describe the same
    /// thing: an app with three notifications in the centre usually also badges its taskbar button
    /// with a three, and adding them would say six. Where only one source knows about an app --
    /// which is the common case, since each sees things the other cannot -- its answer stands
    /// unopposed.
    /// </summary>
    private void Rebuild(DateTimeOffset now)
    {

        var merged = new Dictionary<string, TaskbarBadge>(StringComparer.OrdinalIgnoreCase);

        foreach (var badge in _taskbar.Badges)
            merged[badge.AppUserModelId] = badge;

        foreach (var app in _centre)
        {
            var count = merged.TryGetValue(app.AppUserModelId, out var badge)
                ? Math.Max(badge.Count, app.Count)
                : app.Count;

            merged[app.AppUserModelId] = new TaskbarBadge(app.AppUserModelId, app.AppName, count);
        }

        // Attention last, and only where nothing else already knows about the app: a flash carries
        // no number, so letting it in beside a real count would replace "Outlook 3" with a
        // numberless Outlook chip and lose the part worth reading.
        foreach (var app in _attention)
        {
            if (!merged.ContainsKey(app.AppUserModelId))
                merged[app.AppUserModelId] = new TaskbarBadge(app.AppUserModelId, app.AppName, 0);
        }

        var ranked = merged.Values
            .OrderByDescending(b => b.Count)
            .ThenBy(b => b.AppName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        Sync(ranked.Take(MaxChips).ToList());

        Overflow = Math.Max(0, ranked.Count - MaxChips);
        HasOverflow = Overflow > 0;
        Summary = BuildSummary(ranked, _taskbar.NotificationCentreCount);

        var total = _taskbar.NotificationCentreCount;

        // A badge with no number on it is still one thing waiting. Counting it as zero would make
        // an app going from nothing to a bare dot read as no change at all.
        foreach (var badge in ranked)
            total += Math.Max(1, badge.Count);

        // Only an increase reads as an arrival. A count falling means the user has read something,
        // and flashing at them about their own actions is noise.
        if (total > Count)
        {
            _highlightUntil = now + Highlight;
            IsNew = true;
        }

        Count = total;
        HasCount = total > 0;
    }

    /// <summary>Lets a new arrival stop being new. Rides the island's own tick.</summary>
    public void Tick(DateTimeOffset now)
    {
        if (_highlightUntil is not { } until || now < until)
            return;

        _highlightUntil = null;
        IsNew = false;
    }

    /// <summary>Wipes the reading, for the setting being switched off.</summary>
    public void Clear()
    {
        _taskbar = TaskbarBadgeSnapshot.Empty;
        _centre = [];
        _attention = [];
        _highlightUntil = null;
        IsNew = false;
        Count = 0;
        HasCount = false;
        Overflow = 0;
        HasOverflow = false;
        Summary = string.Empty;
        Badges.Clear();
    }

    private static string BuildSummary(IReadOnlyList<TaskbarBadge> badges, int centreTotal)
    {
        var parts = badges
            .Select(b => b.Count > 0 ? $"{b.AppName} {b.Count}" : b.AppName)
            .ToList();

        if (centreTotal > 0)
            parts.Add($"{centreTotal} in notifications");

        return string.Join(" · ", parts);
    }

    /// <summary>
    /// Brings the collection in line with the reading, touching it only where it differs. The
    /// expanded panel binds to this, and a clear-and-refill on every two-second poll would blink
    /// every row of it whether or not anything moved.
    ///
    /// Written as a positional reconcile rather than the remove-and-move kind, because the
    /// remove-and-move kind could throw and did. Two identical readings in one snapshot -- which
    /// the taskbar walk can produce, since a pinned app can appear in the tree more than once --
    /// computed a Move past the end of a collection that had just been deduplicated down to one
    /// item. The exception surfaced on the UI thread inside a handler that swallows them, so the
    /// count simply never updated, every poll, in silence.
    ///
    /// Nothing here can throw regardless of what the walk hands over. That property is worth more
    /// than the handful of Move calls it gives up.
    /// </summary>
    private void Sync(IReadOnlyList<TaskbarBadge> wanted)
    {
        while (Badges.Count > wanted.Count)
            Badges.RemoveAt(Badges.Count - 1);

        for (var i = 0; i < wanted.Count; i++)
        {
            var badge = wanted[i];

            // Same app in the same place: update the number in place rather than replacing the
            // chip, so the icon does not blink every time a count ticks over.
            if (i < Badges.Count &&
                string.Equals(Badges[i].AppUserModelId, badge.AppUserModelId, StringComparison.OrdinalIgnoreCase))
            {
                Badges[i].Count = badge.Count;
                continue;
            }

            var chip = new BadgeItemViewModel
            {
                AppName = badge.AppName,
                AppUserModelId = badge.AppUserModelId,
                IconPng = Icon(badge.AppUserModelId),
                Count = badge.Count
            };

            if (i < Badges.Count)
                Badges[i] = chip;
            else
                Badges.Add(chip);
        }
    }

    private byte[]? Icon(string appUserModelId)
    {
        if (_icons is null)
            return null;

        if (_iconCache.TryGetValue(appUserModelId, out var cached))
            return cached;

        byte[]? png = null;

        try
        {
            png = _icons.GetAppIconPng(appUserModelId, 32);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // An icon is decoration. Nothing about a shell call going wrong should stop the count
            // that the whole feature is actually for.
        }

        _iconCache[appUserModelId] = png;
        return png;
    }
}
