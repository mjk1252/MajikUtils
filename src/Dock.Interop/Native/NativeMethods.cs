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
}
