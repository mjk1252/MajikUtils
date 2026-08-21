using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Dock.Core.Services;

namespace Dock.Core.ViewModels;

/// <summary>
/// What is waiting for you, on the collapsed pill: an icon per app, with its count beside it.
///
/// Chrome rather than an <see cref="IIslandActivity"/>, and for the same reason as
/// <see cref="ClockViewModel"/> beside it. Activities take turns holding the pill, and something
/// you hid your taskbar to keep cannot afford to lose a turn.
///
/// Two sources feed it, and neither is the taskbar. Reading the taskbar's badges was the original
/// design and was wrong twice over: the shell virtualizes those buttons away while the taskbar is
/// auto-hidden -- so it answered only when it was not needed -- and a badge is whatever an app felt
/// like putting there rather than a count of anything. It is gone.
///
/// <see cref="AppNotifications"/> comes from Windows' own notification centre: real notifications,
/// real counts. <see cref="AttentionRequest"/> comes from windows flashing, which is what an
/// application that draws its own notifications does instead of raising a toast. Between them they
/// cover both kinds of app, and neither cares whether the taskbar is on screen.
/// </summary>
public sealed partial class BadgeCountViewModel : ObservableObject
{
    /// <summary>
    /// How many app chips the collapsed pill draws before giving up and counting the rest.
    ///
    /// Three, because the pill is a strip and the album art, the title and the clock are all
    /// entitled to their share of it. A fourth is rare enough that "+1" is a fair summary and
    /// common enough that it has to say something rather than silently drop it.
    /// </summary>
    public const int MaxChips = 3;

    /// <summary>
    /// How long a new arrival reads as new. Purely visual -- the count is the count either way --
    /// but it is what makes something arriving look different from something still waiting.
    /// </summary>
    public static readonly TimeSpan Highlight = TimeSpan.FromSeconds(4);

    private readonly IIconProvider? _icons;

    /// <summary>
    /// Icons by app id, for the life of the app. Fetching one is a shell call, the sources report
    /// every couple of seconds, and an app's icon does not change -- so it is fetched once, misses
    /// included, since an id that cannot be resolved this minute will not resolve next minute.
    /// </summary>
    private readonly Dictionary<string, byte[]?> _iconCache = new(StringComparer.OrdinalIgnoreCase);

    public BadgeCountViewModel(IIconProvider? icons = null) => _icons = icons;

    /// <summary>
    /// Whether any of this is on the island at all. Off, the pill is exactly what it was before
    /// this existed.
    /// </summary>
    [ObservableProperty] private bool _isEnabled = true;

    /// <summary>How many things are waiting, right now, across every source.</summary>
    [ObservableProperty] private int _count;

    /// <summary>Whether there is anything to draw.</summary>
    [ObservableProperty] private bool _hasCount;

    /// <summary>
    /// True for a few seconds after the count goes up, so the chips can say "this just happened"
    /// rather than "this is still true".
    /// </summary>
    [ObservableProperty] private bool _isNew;

    /// <summary>Everything waiting, broken down, for the tooltip and the expanded panel.</summary>
    [ObservableProperty] private string _summary = string.Empty;

    /// <summary>How many apps did not fit as chips. Zero whenever they all did.</summary>
    [ObservableProperty] private int _overflow;

    [ObservableProperty] private bool _hasOverflow;

    /// <summary>
    /// The last reading from each source, kept apart so either can arrive on its own without
    /// wiping what the other last said. They report on their own schedule and never together.
    /// </summary>
    private IReadOnlyList<AppNotifications> _centre = [];
    private IReadOnlyList<AttentionRequest> _attention = [];

    /// <summary>When the arrival stops reading as new. Null whenever it already has.</summary>
    private DateTimeOffset? _highlightUntil;

    /// <summary>
    /// One chip per app, capped at <see cref="MaxChips"/>, loudest first -- so the app with the
    /// most waiting keeps its place when there is not room for everybody.
    /// </summary>
    public ObservableCollection<BadgeItemViewModel> Badges { get; } = [];

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
    /// Narrower than the other and worth having: an app that draws its own notifications raises no
    /// toast, but it still flashes. It says *that* something wants you and never how many, which is
    /// all Windows knows -- a flash carries no number.
    /// </summary>
    public void Apply(IReadOnlyList<AttentionRequest> attention, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(attention);

        _attention = attention;
        Rebuild(now);
    }

    private void Rebuild(DateTimeOffset now)
    {
        var merged = new Dictionary<string, AppNotifications>(StringComparer.OrdinalIgnoreCase);

        foreach (var app in _centre)
        {
            var key = KeyFor(merged, app.AppUserModelId, app.AppName) ?? app.AppUserModelId;

            var count = merged.TryGetValue(key, out var existing)
                ? Math.Max(existing.Count, app.Count)
                : app.Count;

            merged[key] = app with { Count = count };
        }

        // Attention last, and only where the centre does not already know the app: a flash carries
        // no number, so letting it in beside "Outlook 3" would replace the three with a numberless
        // chip and lose the part worth reading.
        foreach (var app in _attention)
        {
            if (KeyFor(merged, app.AppUserModelId, app.AppName) is null)
                merged[app.AppUserModelId] = new AppNotifications(app.AppUserModelId, app.AppName, 0, string.Empty);
        }

        var ranked = merged.Values
            .OrderByDescending(a => a.Count)
            .ThenBy(a => a.AppName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        Sync(ranked.Take(MaxChips).ToList());

        Overflow = Math.Max(0, ranked.Count - MaxChips);
        HasOverflow = Overflow > 0;
        Summary = BuildSummary(ranked);

        // A flash counts as one thing waiting. Counting it as zero would make an app going from
        // nothing to flashing read as no change at all.
        var total = ranked.Sum(a => Math.Max(1, a.Count));

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

    /// <summary>
    /// Finds an app already in the merge, by id or failing that by name. Null when it is new.
    ///
    /// The name half is not a nicety. The two sources do not agree on what identifies an
    /// application: the notification centre carries an AppUserModelID, and a flashing window
    /// carries only the executable behind it, because that is all a window has. Keyed on the
    /// identifier alone, one Discord arrives as two and draws two chips -- which it did, on screen.
    ///
    /// Two genuinely different applications sharing a display name would merge wrongly. That is a
    /// worse-looking failure than it is a likely one, and much less bad than showing the same app
    /// twice every time it flashes.
    /// </summary>
    private static string? KeyFor(Dictionary<string, AppNotifications> merged, string id, string name)
    {
        if (merged.ContainsKey(id))
            return id;

        if (string.IsNullOrWhiteSpace(name))
            return null;

        foreach (var (key, app) in merged)
        {
            if (string.Equals(app.AppName, name, StringComparison.CurrentCultureIgnoreCase))
                return key;
        }

        return null;
    }

    private static string BuildSummary(IReadOnlyList<AppNotifications> apps) =>
        string.Join(" · ", apps.Select(a => a.Count > 0 ? $"{a.AppName} {a.Count}" : a.AppName));

    /// <summary>
    /// Brings the chips in line with the reading, touching them only where they differ. A
    /// clear-and-refill on every report would blink every chip whether or not anything moved.
    ///
    /// Written as a positional reconcile rather than the remove-and-move kind, because the
    /// remove-and-move kind could throw and did: a duplicate reading computed a Move past the end
    /// of a collection with one item in it, the exception landed on the UI thread inside a handler
    /// that swallows them, and the count silently never updated at all. Nothing here can throw
    /// whatever it is handed, and that is worth more than the Move calls it gives up.
    /// </summary>
    private void Sync(IReadOnlyList<AppNotifications> wanted)
    {
        while (Badges.Count > wanted.Count)
            Badges.RemoveAt(Badges.Count - 1);

        for (var i = 0; i < wanted.Count; i++)
        {
            var app = wanted[i];

            // Same app in the same place: update the number in place rather than replacing the
            // chip, so the icon does not blink every time a count ticks over.
            if (i < Badges.Count &&
                string.Equals(Badges[i].AppUserModelId, app.AppUserModelId, StringComparison.OrdinalIgnoreCase))
            {
                Badges[i].Count = app.Count;
                continue;
            }

            var chip = new BadgeItemViewModel
            {
                AppName = app.AppName,
                AppUserModelId = app.AppUserModelId,
                IconPng = Icon(app.AppUserModelId),
                Count = app.Count
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
