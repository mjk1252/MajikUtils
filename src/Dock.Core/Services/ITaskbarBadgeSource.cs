namespace Dock.Core.Services;

/// <summary>
/// What the taskbar's own buttons are currently badged with: Outlook's unread count, a chat app's
/// pending messages, the notification centre's total.
///
/// This exists for one reason -- an auto-hidden taskbar takes its badges with it. There is no API
/// to read another application's overlay icon, so the reading is taken from the taskbar itself,
/// and the fact that it is a *reading* rather than a subscription is why this is polled: nothing
/// raises an event when a badge changes, and there is no window to hook that would be told either.
///
/// Polled at a couple of seconds, which is the right order for something a person glances at. The
/// walk is not free -- it is a cross-process call into explorer -- so it happens off the UI thread
/// and <see cref="Changed"/> arrives on a pool thread, like the other sources at this level.
/// </summary>
public interface ITaskbarBadgeSource
{
    /// <summary>Raised only when a reading differs from the one before it.</summary>
    event EventHandler<TaskbarBadgeSnapshot>? Changed;

    void Start();
    void Stop();
}

/// <param name="Badges">One entry per taskbar button currently wearing a badge, in taskbar order.
/// Empty when nothing is badged, which is the common case.</param>
/// <param name="NotificationCentreCount">Unread notifications in the notification centre as a
/// whole. A different question from any one app's badge and worth keeping separate: an app can
/// hold a badge with nothing in the centre, and the centre can be full of things from apps that
/// never badge at all.</param>
public sealed record TaskbarBadgeSnapshot(
    IReadOnlyList<TaskbarBadge> Badges,
    int NotificationCentreCount)
{
    public static readonly TaskbarBadgeSnapshot Empty = new([], 0);

    public bool IsEmpty => Badges.Count == 0 && NotificationCentreCount == 0;
}

/// <param name="AppUserModelId">The button's own AppUserModelID, which is how the taskbar
/// identifies it and the handle anything wanting to raise that app would need.</param>
/// <param name="AppName">The app's display name as the taskbar gives it: "Microsoft Outlook".</param>
/// <param name="Count">How many. Zero for a badge that carries no number -- a dot rather than a
/// count -- which is a real state and not the same as having no badge at all.</param>
public readonly record struct TaskbarBadge(string AppUserModelId, string AppName, int Count);
