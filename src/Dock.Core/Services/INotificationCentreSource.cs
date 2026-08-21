namespace Dock.Core.Services;

/// <summary>
/// What is sitting in Windows' own notification centre, per application.
///
/// The source this feature should have started with. Reading the taskbar's buttons was the obvious
/// route and the wrong one twice over: the shell virtualizes those buttons away while the taskbar
/// is auto-hidden, which is precisely the case the feature exists for, and even on screen a badge
/// is whatever the app felt like putting there rather than a count of anything.
///
/// This asks the system instead. Every entry is a real notification with a real app behind it, so
/// there is no string to parse and no badge convention to guess at -- and it is unaffected by
/// whether the taskbar is drawn, because the notification centre is not the taskbar.
///
/// It covers exactly what raises a Windows toast, which is not everything: an application drawing
/// its own notifications in its own window is invisible here, and correctly so -- Windows does not
/// know about it either.
/// </summary>
public interface INotificationCentreSource
{
    /// <summary>Raised only when a reading differs from the one before it.</summary>
    event EventHandler<IReadOnlyList<AppNotifications>>? Changed;

    /// <summary>
    /// Whether the user has let this app read notifications. False leaves everything else here
    /// quiet rather than failing: this is a permission, and being told no is an ordinary answer.
    /// </summary>
    bool IsAllowed { get; }

    void Start();
    void Stop();
}

/// <param name="AppUserModelId">The app the notifications came from, as Windows identifies it --
/// the same id a taskbar button carries, so an icon can be resolved for it the same way.</param>
/// <param name="AppName">Its display name: "Microsoft Outlook".</param>
/// <param name="Count">How many of its notifications are waiting. A real count of real
/// notifications, not a badge convention.</param>
/// <param name="Latest">The newest one's text, for the tooltip. Empty when there is nothing worth
/// showing -- a notification with no text is unusual but not impossible.</param>
public readonly record struct AppNotifications(
    string AppUserModelId,
    string AppName,
    int Count,
    string Latest);
