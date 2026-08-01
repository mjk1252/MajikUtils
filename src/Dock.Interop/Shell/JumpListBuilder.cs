using System.Runtime.InteropServices;
using Dock.Interop.Native;

namespace Dock.Interop.Shell;

/// <summary>One entry in a taskbar button's right-click menu.</summary>
/// <param name="Title">Label shown in the menu.</param>
/// <param name="Target">Executable the entry launches.</param>
/// <param name="Arguments">Command line passed to it.</param>
/// <param name="IconResource">"path,index" for the entry's icon, or null for the target's own.</param>
public sealed record JumpListTask(string Title, string Target, string Arguments = "", string? IconResource = null);

/// <summary>
/// Builds the Tasks section of a taskbar button's jump list.
///
/// WPF's <c>System.Windows.Shell.JumpList</c> cannot be used here: it targets the process
/// AppUserModelID, so it can only ever describe one list, whereas each of Dock's buttons carries
/// its own ID. Driving ICustomDestinationList directly is what allows a per-button list, via the
/// SetAppID call WPF never exposes.
///
/// Jump lists live in the shell, not in the process, so entries registered here keep working on a
/// pinned button after Dock has exited -- which is the point: that is how "Exit Dock" can sit on a
/// button whose window is long gone.
/// </summary>
public static class JumpListBuilder
{
    public static void Apply(string appId, IReadOnlyList<JumpListTask> tasks)
    {
        if (tasks.Count == 0)
            return;

        ICustomDestinationListWrapper? list = null;
        try
        {
            list = ICustomDestinationListWrapper.Create(appId);
            if (list is null)
                return;

            list.AddTasks(tasks);
            list.Commit();
        }
        catch (COMException)
        {
            // A jump list is pure convenience -- every task it offers is reachable another way, so
            // a shell that refuses to take one costs nothing but the shortcut.
            list?.Abort();
        }
        finally
        {
            list?.Dispose();
        }
    }

    /// <summary>
    /// Owns the BeginList/CommitList pair and the COM objects involved. Wrapped in a type of its
    /// own purely so the release order stays right no matter which step throws: an uncommitted,
    /// unaborted destination list holds a lock on that AppUserModelID's list for the process's
    /// lifetime.
    /// </summary>
    private sealed class ICustomDestinationListWrapper : IDisposable
    {
        private readonly NativeMethods.ICustomDestinationList _list;
        private readonly List<object> _comObjects = [];

        private ICustomDestinationListWrapper(NativeMethods.ICustomDestinationList list)
        {
            _list = list;
            _comObjects.Add(list);
        }

        public static ICustomDestinationListWrapper? Create(string appId)
        {
            var type = Type.GetTypeFromCLSID(NativeMethods.CLSID_DestinationList);
            if (type is null || Activator.CreateInstance(type) is not NativeMethods.ICustomDestinationList list)
                return null;

            list.SetAppID(appId);

            // BeginList must precede AddUserTasks even though its "removed destinations" result is
            // of no interest to a Tasks-only list -- the shell rejects the list otherwise.
            var iid = NativeMethods.IID_IObjectArray;
            if (list.BeginList(out _, ref iid, out var removed) != 0)
            {
                Marshal.ReleaseComObject(list);
                return null;
            }

            var wrapper = new ICustomDestinationListWrapper(list);
            if (removed is not null)
                wrapper._comObjects.Add(removed);

            return wrapper;
        }

        public void AddTasks(IReadOnlyList<JumpListTask> tasks)
        {
            var collectionType = Type.GetTypeFromCLSID(NativeMethods.CLSID_EnumerableObjectCollection);
            if (collectionType is null ||
                Activator.CreateInstance(collectionType) is not NativeMethods.IObjectCollection collection)
            {
                return;
            }

            _comObjects.Add(collection);

            foreach (var task in tasks)
            {
                if (CreateShellLink(task) is { } link)
                    collection.AddObject(link);
            }

            if (collection is NativeMethods.IObjectArray array)
                _list.AddUserTasks(array);
        }

        private NativeMethods.IShellLinkW? CreateShellLink(JumpListTask task)
        {
            var linkType = Type.GetTypeFromCLSID(NativeMethods.CLSID_ShellLink);
            if (linkType is null || Activator.CreateInstance(linkType) is not NativeMethods.IShellLinkW link)
                return null;

            _comObjects.Add(link);

            link.SetPath(task.Target);
            link.SetArguments(task.Arguments);

            if (task.IconResource is { } icon && SplitIconResource(icon) is var (iconPath, iconIndex))
                link.SetIconLocation(iconPath, iconIndex);

            // The visible label is a property on the link, not its description: SetDescription only
            // sets the tooltip, and a link with no title renders in the menu as its target's file
            // name -- every Dock task would read "Dock".
            if (link is NativeMethods.IPropertyStore store)
            {
                var key = new NativeMethods.PROPERTYKEY(NativeMethods.PKEY_Title_Format, NativeMethods.PID_Title);
                var value = new NativeMethods.PROPVARIANT
                {
                    vt = NativeMethods.VT_LPWSTR,
                    data = Marshal.StringToCoTaskMemUni(task.Title)
                };

                try
                {
                    store.SetValue(ref key, ref value);
                    store.Commit();
                }
                finally
                {
                    NativeMethods.PropVariantClear(ref value);
                }
            }

            return link;
        }

        private static (string Path, int Index) SplitIconResource(string resource)
        {
            var comma = resource.LastIndexOf(',');
            if (comma > 0 && int.TryParse(resource[(comma + 1)..], out var index))
                return (resource[..comma], index);

            return (resource, 0);
        }

        public void Commit() => _list.CommitList();

        public void Abort() => _list.AbortList();

        public void Dispose()
        {
            // Reverse order so the destination list itself, added first, is released last.
            for (var i = _comObjects.Count - 1; i >= 0; i--)
            {
                if (Marshal.IsComObject(_comObjects[i]))
                    Marshal.ReleaseComObject(_comObjects[i]);
            }

            _comObjects.Clear();
        }
    }
}
