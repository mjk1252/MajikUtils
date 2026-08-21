using System.Runtime.InteropServices;

namespace Dock.Interop.Native;

internal static class NativeMethods
{
    internal const uint SHGFI_ICON = 0x100;
    internal const uint SHGFI_LARGEICON = 0x0;
    internal const uint SHGFI_SMALLICON = 0x1;
    internal const uint SHGFI_SYSICONINDEX = 0x4000;

    // SHGFI_LARGEICON tops out at the system large-icon metric (32px at 100% scaling), so anything
    // rendered bigger than that has to come from the shell's own extra-large/jumbo image lists.
    internal const int SHIL_EXTRALARGE = 2;
    internal const int SHIL_JUMBO = 4;

    internal const int ILD_TRANSPARENT = 1;

    internal static Guid IID_IImageList = new("46EB5926-582E-4017-9FDF-E8998DAA0950");

    /// <summary>
    /// Only declared as far as GetIcon -- the members before it exist purely to place GetIcon at
    /// its correct vtable slot and are never called, so Draw's parameter is left as an opaque
    /// pointer rather than dragging in the whole IMAGELISTDRAWPARAMS layout.
    /// </summary>
    [ComImport]
    [Guid("46EB5926-582E-4017-9FDF-E8998DAA0950")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IImageList
    {
        [PreserveSig] int Add(IntPtr hbmImage, IntPtr hbmMask, ref int pi);
        [PreserveSig] int ReplaceIcon(int i, IntPtr hicon, ref int pi);
        [PreserveSig] int SetOverlayImage(int iImage, int iOverlay);
        [PreserveSig] int Replace(int i, IntPtr hbmImage, IntPtr hbmMask);
        [PreserveSig] int AddMasked(IntPtr hbmImage, int crMask, ref int pi);
        [PreserveSig] int Draw(IntPtr pimldp);
        [PreserveSig] int Remove(int i);
        [PreserveSig] int GetIcon(int i, int flags, ref IntPtr picon);
    }

    // Exported by ordinal only; there is no named export for this on any Windows version.
    [DllImport("shell32.dll", EntryPoint = "#727")]
    internal static extern int SHGetImageList(int imageList, ref Guid riid, out IImageList ppv);

    internal delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WNDCLASS
    {
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern ushort RegisterClass(ref WNDCLASS wndClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr CreateWindowEx(
        uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    internal static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    internal static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern bool GetCursorPos(out POINT point);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr GetModuleHandle(string? moduleName);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    internal struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr SHGetFileInfo(string path, uint fileAttributes, ref SHFILEINFO fileInfo, uint size, uint flags);

    /// <summary>
    /// Says the first argument is an item id list rather than a path, which is the only way to ask
    /// about something that has no path -- a packaged app, or anything else that exists only as an
    /// entry in the shell's Applications folder.
    /// </summary>
    internal const uint SHGFI_PIDL = 0x8;

    /// <summary>
    /// The same call again, taking a PIDL. Two declarations rather than one taking IntPtr, because
    /// the string overload has to marshal as Unicode and the PIDL one must not be marshalled at
    /// all.
    /// </summary>
    [DllImport("shell32.dll", EntryPoint = "SHGetFileInfoW")]
    internal static extern IntPtr SHGetFileInfoPidl(IntPtr pidl, uint fileAttributes, ref SHFILEINFO fileInfo, uint size, uint flags);

    /// <summary>
    /// Turns a shell path into a PIDL. <c>shell:AppsFolder\&lt;AppUserModelID&gt;</c> is the form
    /// that matters here: it is how anything holding only an AppUserModelID -- a taskbar button,
    /// say -- reaches the item the shell knows under that id.
    /// </summary>
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern int SHParseDisplayName(
        string name, IntPtr bindContext, out IntPtr pidl, uint attributesIn, out uint attributesOut);

    [DllImport("user32.dll")]
    internal static extern bool DestroyIcon(IntPtr handle);

    internal const int WM_HOTKEY = 0x0312;
    internal const uint MOD_ALT = 0x0001;
    internal const uint MOD_CONTROL = 0x0002;
    internal const uint MOD_SHIFT = 0x0004;
    internal const uint VK_V = 0x56;

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll")]
    internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    internal const int WM_CLIPBOARDUPDATE = 0x031D;

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool AddClipboardFormatListener(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool RemoveClipboardFormatListener(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    // --- Per-window AppUserModelID (taskbar button identity) ---------------------------------

    /// <summary>
    /// All four of the properties below live in this one format GUID and are told apart only by
    /// their property id, so they share a single fmtid rather than getting one PROPERTYKEY
    /// constant each with a duplicated Guid literal.
    /// </summary>
    internal static readonly Guid PKEY_AppUserModel = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");

    internal const uint PID_AppUserModel_RelaunchCommand = 2;
    internal const uint PID_AppUserModel_RelaunchIconResource = 3;
    internal const uint PID_AppUserModel_RelaunchDisplayNameResource = 4;
    internal const uint PID_AppUserModel_ID = 5;

    internal static Guid IID_IPropertyStore = new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");

    [StructLayout(LayoutKind.Sequential)]
    internal struct PROPERTYKEY(Guid formatId, uint propertyId)
    {
        public Guid fmtid = formatId;
        public uint pid = propertyId;
    }

    internal const ushort VT_LPWSTR = 31;

    /// <summary>
    /// Deliberately opaque past the leading discriminant and the first pointer: every value we
    /// store is a VT_LPWSTR, whose payload is exactly that one pointer. The trailing field exists
    /// purely to give the struct its true 24-byte (x64) size.
    ///
    /// Built by hand rather than through propsys's InitPropVariantFromString, which despite its
    /// name is an inline helper in propvarutil.h and is not exported by propsys.dll at all --
    /// P/Invoking it throws EntryPointNotFoundException. <see cref="PropVariantClear"/> is a real
    /// export, and frees the string with CoTaskMemFree, which is what
    /// <c>Marshal.StringToCoTaskMemUni</c> allocates with.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct PROPVARIANT
    {
        public ushort vt;
        public ushort wReserved1;
        public ushort wReserved2;
        public ushort wReserved3;
        public IntPtr data;
        public IntPtr dataHigh;
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint propCount);
        [PreserveSig] int GetAt(uint index, out PROPERTYKEY key);
        [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
        [PreserveSig] int SetValue(ref PROPERTYKEY key, ref PROPVARIANT value);
        [PreserveSig] int Commit();
    }

    [DllImport("shell32.dll")]
    internal static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IPropertyStore propertyStore);

    [DllImport("ole32.dll")]
    internal static extern int PropVariantClear(ref PROPVARIANT propVariant);

    // --- Monitors -----------------------------------------------------------------------------

    internal const uint MONITOR_DEFAULTTOPRIMARY = 1;
    internal const uint MONITOR_DEFAULTTONEAREST = 2;
    internal const int MDT_EFFECTIVE_DPI = 0;

    internal const uint SWP_NOZORDER = 0x0004;
    internal const uint SWP_NOACTIVATE = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    /// <summary>
    /// MONITORINFO plus the adapter's device name (<c>\\.\DISPLAY1</c>), which is the only stable
    /// handle on a particular screen: HMONITORs are recycled across display changes, and indices
    /// shift the moment a monitor is unplugged.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    internal const uint MONITORINFOF_PRIMARY = 1;

    internal delegate bool MonitorEnumProc(IntPtr monitor, IntPtr dc, ref RECT rect, IntPtr data);

    [DllImport("user32.dll")]
    internal static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip,
        MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    internal static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFOEX info);

    [DllImport("user32.dll")]
    internal static extern IntPtr MonitorFromPoint(POINT point, uint flags);

    [DllImport("user32.dll")]
    internal static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    internal static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [DllImport("shcore.dll")]
    internal static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter,
        int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    internal static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    /// <summary>The desktop's own window. Always "full screen", and never a reason to hide anything.</summary>
    [DllImport("user32.dll")]
    internal static extern IntPtr GetShellWindow();

    // --- Extended window styles (the media island's overlay behaviour) -------------------------

    internal const int GWL_EXSTYLE = -20;

    /// <summary>The ordinary window styles, as opposed to the extended ones.</summary>
    internal const int GWL_STYLE = -16;

    /// <summary>
    /// A title bar. The one thing that reliably tells a merely maximised window from a genuinely
    /// full-screen one: a window that has gone full-screen drops its caption, and a maximised one
    /// keeps it however much of the monitor it happens to cover.
    /// </summary>
    internal const long WS_CAPTION = 0x00C00000;

    /// <summary>Clicks fall through to whatever is underneath.</summary>
    internal const int WS_EX_TRANSPARENT = 0x00000020;

    /// <summary>Keeps the window out of Alt+Tab.</summary>
    internal const int WS_EX_TOOLWINDOW = 0x00000080;

    /// <summary>The window never takes the foreground, so hovering it cannot steal focus.</summary>
    internal const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    internal static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    internal static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    // --- Jump lists ---------------------------------------------------------------------------
    //
    // WPF's own System.Windows.Shell.JumpList targets the *process* AppUserModelID, so it can only
    // ever describe one list. Dock's taskbar buttons each carry their own ID, so building per-button
    // lists means driving ICustomDestinationList directly and calling SetAppID on it.

    /// <summary>Title shown for a jump-list task, set on the shortcut's own property store.</summary>
    internal static readonly Guid PKEY_Title_Format = new("F29F85E0-4FF9-1068-AB91-08002B27B3D9");
    internal const uint PID_Title = 2;

    internal static readonly Guid CLSID_DestinationList = new("77F10CF0-3DB5-4966-B520-B7C54FD35ED6");
    internal static readonly Guid CLSID_EnumerableObjectCollection = new("2D3468C1-36A7-43B6-AC24-D3F02FD9607A");
    internal static readonly Guid CLSID_ShellLink = new("00021401-0000-0000-C000-000000000046");
    internal static Guid IID_IObjectArray = new("92CA9DCD-5622-4BBA-A805-5E9F541BD8C9");

    [ComImport]
    [Guid("92CA9DCD-5622-4BBA-A805-5E9F541BD8C9")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IObjectArray
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object item);
    }

    /// <summary>
    /// Redeclares IObjectArray's two members rather than inheriting the interface: a COM interop
    /// interface's vtable is built from the members declared on it alone, so inheriting would
    /// silently place AddObject at slot 3 instead of 5.
    /// </summary>
    [ComImport]
    [Guid("5632B1A4-E38A-400A-928A-D4CD63230295")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IObjectCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object item);
        [PreserveSig] int AddObject([MarshalAs(UnmanagedType.Interface)] object item);
        [PreserveSig] int AddFromArray([MarshalAs(UnmanagedType.Interface)] IObjectArray source);
        [PreserveSig] int RemoveObjectAt(uint index);
        [PreserveSig] int Clear();
    }

    [ComImport]
    [Guid("6332DEBF-87B5-4670-90C0-5E57B408A49E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ICustomDestinationList
    {
        [PreserveSig] int SetAppID([MarshalAs(UnmanagedType.LPWStr)] string appId);
        [PreserveSig] int BeginList(out uint minSlots, ref Guid iid,
            [MarshalAs(UnmanagedType.Interface)] out object removed);
        [PreserveSig] int AppendCategory([MarshalAs(UnmanagedType.LPWStr)] string category, IObjectArray items);
        [PreserveSig] int AppendKnownCategory(int category);
        [PreserveSig] int AddUserTasks([MarshalAs(UnmanagedType.Interface)] IObjectArray items);
        [PreserveSig] int CommitList();
        [PreserveSig] int GetRemovedDestinations(ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object removed);
        [PreserveSig] int DeleteList([MarshalAs(UnmanagedType.LPWStr)] string appId);
        [PreserveSig] int AbortList();
    }

    /// <summary>
    /// Only the setters Dock needs are given real signatures; the rest exist to hold their vtable
    /// slots. Getters take a buffer we never supply, so they are declared with opaque parameters
    /// rather than marshalling that could not work if it were ever called.
    /// </summary>
    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellLinkW
    {
        [PreserveSig] int GetPath(IntPtr file, int cch, IntPtr findData, uint flags);
        [PreserveSig] int GetIDList(out IntPtr idList);
        [PreserveSig] int SetIDList(IntPtr idList);
        [PreserveSig] int GetDescription(IntPtr name, int cch);
        [PreserveSig] int SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int GetWorkingDirectory(IntPtr dir, int cch);
        [PreserveSig] int SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        [PreserveSig] int GetArguments(IntPtr args, int cch);
        [PreserveSig] int SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        [PreserveSig] int GetHotkey(out ushort hotkey);
        [PreserveSig] int SetHotkey(ushort hotkey);
        [PreserveSig] int GetShowCmd(out int showCmd);
        [PreserveSig] int SetShowCmd(int showCmd);
        [PreserveSig] int GetIconLocation(IntPtr iconPath, int cch, out int iconIndex);
        [PreserveSig] int SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
        [PreserveSig] int SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pathRel, uint reserved);
        [PreserveSig] int Resolve(IntPtr hwnd, uint flags);
        [PreserveSig] int SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }
}
