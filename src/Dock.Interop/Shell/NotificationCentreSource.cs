using Dock.Core.Services;
using Windows.Foundation.Metadata;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace Dock.Interop.Shell;

/// <summary>
/// Reads Windows' notification centre through <see cref="UserNotificationListener"/>.
///
/// Documented as needing package identity and the <c>userNotificationListener</c> capability, which
/// this application has neither of -- and it works anyway, unpackaged and unsigned, returning
/// access <c>Allowed</c> and real notifications. That was measured on the machine this was written
/// on rather than assumed, and it is measured again at runtime: <see cref="IsAllowed"/> is what
/// <see cref="RequestAccessAsync"/> actually said, not what the documentation implies, and
/// everything here stays quiet when the answer is no.
///
/// Polled rather than subscribed. <c>NotificationChanged</c> exists and is the obvious thing to
/// use, but it is one of the parts that genuinely does depend on package identity, and an event
/// that silently never fires is worse than a poll that plainly works.
/// </summary>
public sealed class NotificationCentreSource : INotificationCentreSource, IDisposable
{
    /// <summary>
    /// How often the centre is read. A notification is something a person reacts to in seconds, and
    /// the call is a local system query rather than a walk of another process's UI.
    /// </summary>
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    private readonly Lock _gate = new();

    private Timer? _timer;
    private IReadOnlyList<AppNotifications> _last = [];
    private int _reading;

    public event EventHandler<IReadOnlyList<AppNotifications>>? Changed;

    public bool IsAllowed { get; private set; }

    /// <summary>
    /// Asks for permission and starts reading if it is given.
    ///
    /// Asynchronous because the permission prompt is, and swallowing on failure because every way
    /// this can fail -- the API missing on an older build, the user saying no, the listener being
    /// unavailable to an unpackaged caller after all -- has the same right answer: no notifications
    /// from this source, and an island that carries on doing everything else.
    /// </summary>
    public async void Start()
    {
        try
        {
            if (!ApiInformation.IsTypePresent("Windows.UI.Notifications.Management.UserNotificationListener"))
                return;

            var listener = UserNotificationListener.Current;
            if (listener is null)
                return;

            IsAllowed = await listener.RequestAccessAsync() == UserNotificationListenerAccessStatus.Allowed;

            if (!IsAllowed)
                return;

            lock (_gate)
                _timer ??= new Timer(_ => Read(listener), null, TimeSpan.Zero, Interval);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            IsAllowed = false;
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
        }

        // So a restart reports what it finds rather than comparing against a stale reading and
        // staying silent about notifications that have been there the whole time.
        _last = [];
    }

    public void Dispose() => Stop();

    private async void Read(UserNotificationListener listener)
    {
        if (Interlocked.Exchange(ref _reading, 1) == 1)
            return;

        try
        {
            var notifications = await listener.GetNotificationsAsync(NotificationKinds.Toast);

            var byApp = new Dictionary<string, (string Name, int Count, string Latest, DateTimeOffset When)>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var notification in notifications)
            {
                var info = notification.AppInfo;
                var id = info?.AppUserModelId;

                if (string.IsNullOrEmpty(id))
                    continue;

                var name = info?.DisplayInfo?.DisplayName ?? id;
                var text = FirstText(notification);

                if (byApp.TryGetValue(id, out var existing))
                {
                    // Newest wins for the text, so the tooltip says the most recent thing rather
                    // than whichever happened to be enumerated first.
                    var newer = notification.CreationTime > existing.When;

                    byApp[id] = (
                        existing.Name,
                        existing.Count + 1,
                        newer ? text : existing.Latest,
                        newer ? notification.CreationTime : existing.When);
                }
                else
                {
                    byApp[id] = (name, 1, text, notification.CreationTime);
                }
            }

            var snapshot = byApp
                .Select(kv => new AppNotifications(kv.Key, kv.Value.Name, kv.Value.Count, kv.Value.Latest))
                .OrderByDescending(a => a.Count)
                .ToList();

            if (snapshot.SequenceEqual(_last))
                return;

            _last = snapshot;
            Changed?.Invoke(this, snapshot);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The centre is another process's business and can be mid-change. Trying again in two
            // seconds is the whole of the recovery, and reporting nothing keeps the last good
            // reading rather than blanking the island over a transient failure.
        }
        finally
        {
            Interlocked.Exchange(ref _reading, 0);
        }
    }

    /// <summary>
    /// The notification's own text, title first. Bindings are a toast's layout rather than its
    /// content, so this flattens them and takes what a person would read first.
    /// </summary>
    private static string FirstText(UserNotification notification)
    {
        try
        {
            var elements = notification.Notification?.Visual?.Bindings?
                .SelectMany(b => b.GetTextElements())
                .Select(t => t.Text)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .ToList();

            return elements is { Count: > 0 } ? string.Join(" — ", elements.Take(2)) : string.Empty;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return string.Empty;
        }
    }
}
