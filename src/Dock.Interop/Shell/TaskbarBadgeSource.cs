using System.Runtime.InteropServices;
using System.Windows.Automation;
using Dock.Core.Models;
using Dock.Core.Services;

namespace Dock.Interop.Shell;

/// <summary>
/// Reads the taskbar's badges by walking its accessibility tree.
///
/// There is no supported way to ask what overlay icon another process put on its taskbar button --
/// <c>ITaskbarList3::SetOverlayIcon</c> is a setter and nothing reads it back. What the shell does
/// publish is an accessible name per button, and that name spells the badge out in words. So this
/// walks explorer's tree, hands each button's name and automation id to
/// <see cref="TaskbarButtonName"/>, and reports what comes back.
///
/// Which makes it exactly as sturdy as that tree, and the code is written for that: every call is
/// wrapped, an explorer that restarts is picked up on the next pass by re-finding the tray from
/// the root, and a pass that fails entirely reports nothing rather than throwing into a timer
/// callback nobody is catching.
///
/// Both taskbars are walked. <c>Shell_TrayWnd</c> is the primary monitor's and
/// <c>Shell_SecondaryTrayWnd</c> is every other one, and an app pinned to a second screen badges
/// there and nowhere else.
/// </summary>
public sealed class TaskbarBadgeSource : ITaskbarBadgeSource, IDisposable
{
    /// <summary>
    /// How often the tree is walked. Slow enough that a cross-process walk costs nothing anyone
    /// can measure, fast enough that a badge appearing feels like it appeared -- this stands in
    /// for a taskbar, and a taskbar updates while you are looking at it.
    /// </summary>
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Fetches every property in one cross-process round trip instead of three per element.
    /// With twenty-odd buttons on the taskbar that is the difference between a walk worth doing
    /// every two seconds and one that is not.
    /// </summary>
    private static readonly CacheRequest Cached = BuildCacheRequest();

    private readonly Lock _gate = new();

    private Timer? _timer;
    private TaskbarBadgeSnapshot _last = TaskbarBadgeSnapshot.Empty;

    /// <summary>Guards against a slow walk overlapping the next tick.</summary>
    private int _walking;

    public event EventHandler<TaskbarBadgeSnapshot>? Changed;

    public void Start()
    {
        lock (_gate)
        {
            // Timer rather than DispatcherTimer: UI Automation calls block on another process
            // answering, and the island's own thread is the one thread that must never wait on
            // explorer.
            _timer ??= new Timer(_ => Walk(), null, TimeSpan.Zero, Interval);
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
        // staying silent about a badge that has been there the whole time.
        _last = TaskbarBadgeSnapshot.Empty;
    }

    public void Dispose() => Stop();

    private void Walk()
    {
        if (Interlocked.Exchange(ref _walking, 1) == 1)
            return;

        try
        {
            var snapshot = Read();

            if (snapshot is null || Same(snapshot, _last))
                return;

            _last = snapshot;
            Changed?.Invoke(this, snapshot);
        }
        finally
        {
            Interlocked.Exchange(ref _walking, 0);
        }
    }

    /// <summary>
    /// One pass over both taskbars. Null -- as distinct from an empty snapshot -- means the walk
    /// itself failed, and the difference matters: an empty snapshot says there are no badges and
    /// would take a dot off the island, where a failed walk says nothing and leaves the last good
    /// reading standing.
    /// </summary>
    private static TaskbarBadgeSnapshot? Read()
    {
        try
        {
            var badges = new List<TaskbarBadge>();
            var centre = 0;
            var found = false;

            foreach (var tray in Trays())
            {
                found = true;

                using (Cached.Activate())
                {
                    foreach (AutomationElement element in tray.FindAll(
                        TreeScope.Descendants,
                        new PropertyCondition(
                            AutomationElement.ControlTypeProperty, ControlType.Button)))
                    {
                        var name = element.Cached.Name;

                        // Empty for the tray's own buttons, which is exactly how the parser tells
                        // an app button from Start, Widgets or Show Desktop.
                        var id = element.Cached.AutomationId;

                        // Where the badge actually is. The name never mentions it -- Discord's
                        // button reads "Discord - 1 running window pinned" whether or not it has
                        // unread messages, and says "Unread messages" here instead.
                        var help = element.Cached.HelpText;

                        if (TaskbarButtonName.ReadBadge(name, id, help) is { } badge)
                            badges.Add(badge);

                        centre = Math.Max(centre, TaskbarButtonName.ReadNotificationCentreCount(name));
                    }
                }
            }

            // One entry per app, keeping the loudest. A pinned app can turn up more than once in
            // the tree -- the walk sees Discord twice on this machine -- and counting it twice
            // would inflate the total by however many times the shell happens to expose it.
            var deduplicated = badges
                .GroupBy(b => b.AppUserModelId, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(b => b.Count).First())
                .ToList();

            return found ? new TaskbarBadgeSnapshot(deduplicated, centre) : null;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException
                                      or TimeoutException or COMException)
        {
            // Explorer restarting, or a button that went away mid-walk. Both are ordinary, and
            // both are answered by trying again in two seconds.
            return null;
        }
    }

    /// <summary>
    /// The taskbar windows, re-found from the root every pass rather than cached. Caching the
    /// element would be the obvious optimisation and the wrong one: explorer restarts, and a held
    /// <see cref="AutomationElement"/> pointing at the taskbar that used to exist throws on every
    /// call from then on with no way back.
    /// </summary>
    private static IEnumerable<AutomationElement> Trays()
    {
        var root = AutomationElement.RootElement;

        var condition = new OrCondition(
            new PropertyCondition(AutomationElement.ClassNameProperty, "Shell_TrayWnd"),
            new PropertyCondition(AutomationElement.ClassNameProperty, "Shell_SecondaryTrayWnd"));

        foreach (AutomationElement tray in root.FindAll(TreeScope.Children, condition))
            yield return tray;
    }

    private static CacheRequest BuildCacheRequest()
    {
        var request = new CacheRequest { TreeScope = TreeScope.Element };
        request.Add(AutomationElement.NameProperty);
        request.Add(AutomationElement.AutomationIdProperty);
        request.Add(AutomationElement.HelpTextProperty);
        request.AutomationElementMode = AutomationElementMode.None;

        return request;
    }

    private static bool Same(TaskbarBadgeSnapshot a, TaskbarBadgeSnapshot b) =>
        a.NotificationCentreCount == b.NotificationCentreCount &&
        a.Badges.SequenceEqual(b.Badges);
}
