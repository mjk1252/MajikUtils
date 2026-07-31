using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Dock.Core.Services;
using Dock.Interop.Native;

namespace Dock.Interop.Shell;

public sealed class ShellIconProvider : IIconProvider
{
    public byte[]? GetIconPng(string path, int size)
    {
        var flags = NativeMethods.SHGFI_ICON | (size <= 16 ? NativeMethods.SHGFI_SMALLICON : NativeMethods.SHGFI_LARGEICON);
        var info = new NativeMethods.SHFILEINFO();

        var result = NativeMethods.SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<NativeMethods.SHFILEINFO>(), flags);
        if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero)
            return null;

        try
        {
            using var icon = Icon.FromHandle(info.hIcon);
            using var bitmap = icon.ToBitmap();
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }
        finally
        {
            NativeMethods.DestroyIcon(info.hIcon);
        }
    }
}
