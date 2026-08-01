using System.Runtime.InteropServices;
using Dock.Interop.Native;

namespace Dock.Interop.Shell;

/// <summary>
/// Gives a window its own identity on the taskbar.
///
/// Windows groups taskbar buttons by AppUserModelID, so two windows from one process share a
/// single button unless each HWND carries a distinct ID. Stamping the ID also makes the button
/// pinnable on its own -- but a pinned shortcut has no window to point at, so the three
/// Relaunch* properties are what tell the shell how to start the app back up, and what to call
/// and draw the pinned entry in the meantime. Setting the ID without them yields a button that
/// pins and then fails to launch.
/// </summary>
public static class AppIdRegistrar
{
    /// <summary>
    /// Must be called before the window is first shown: the shell reads these properties when it
    /// creates the taskbar button, and never re-reads them for that HWND afterwards.
    /// </summary>
    public static void Stamp(IntPtr hwnd, string appId, string relaunchCommand, string displayName, string iconResource)
    {
        NativeMethods.IPropertyStore? store = null;
        try
        {
            var iid = NativeMethods.IID_IPropertyStore;
            if (NativeMethods.SHGetPropertyStoreForWindow(hwnd, ref iid, out store) != 0 || store is null)
                return;

            SetString(store, NativeMethods.PID_AppUserModel_ID, appId);
            SetString(store, NativeMethods.PID_AppUserModel_RelaunchCommand, relaunchCommand);
            SetString(store, NativeMethods.PID_AppUserModel_RelaunchDisplayNameResource, displayName);
            SetString(store, NativeMethods.PID_AppUserModel_RelaunchIconResource, iconResource);
            store.Commit();
        }
        catch (Exception ex) when (ex is COMException or EntryPointNotFoundException or DllNotFoundException)
        {
            // Degrade rather than fail: without the stamp both windows share one taskbar button
            // and can't be pinned separately, but every panel still works.
        }
        finally
        {
            if (store is not null)
                Marshal.ReleaseComObject(store);
        }
    }

    private static void SetString(NativeMethods.IPropertyStore store, uint propertyId, string value)
    {
        var variant = new NativeMethods.PROPVARIANT
        {
            vt = NativeMethods.VT_LPWSTR,
            data = Marshal.StringToCoTaskMemUni(value)
        };

        try
        {
            var key = new NativeMethods.PROPERTYKEY(NativeMethods.PKEY_AppUserModel, propertyId);
            store.SetValue(ref key, ref variant);
        }
        finally
        {
            // Frees the string too -- PropVariantClear dispatches on vt and CoTaskMemFrees a
            // VT_LPWSTR's payload, which is the same allocator StringToCoTaskMemUni used.
            NativeMethods.PropVariantClear(ref variant);
        }
    }
}
