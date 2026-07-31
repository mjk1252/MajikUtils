using System.Runtime.InteropServices;
using Dock.Interop.Native;

namespace Dock.Interop.Shell;

public static class IconHandles
{
    public static IntPtr GetHIcon(string path, bool small = true)
    {
        var flags = NativeMethods.SHGFI_ICON | (small ? NativeMethods.SHGFI_SMALLICON : NativeMethods.SHGFI_LARGEICON);
        var info = new NativeMethods.SHFILEINFO();
        NativeMethods.SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<NativeMethods.SHFILEINFO>(), flags);
        return info.hIcon;
    }
}
