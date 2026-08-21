using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Dock.Core.Services;
using Dock.Interop.Native;

namespace Dock.Interop.Shell;

public sealed class ShellIconProvider : IIconProvider
{
    // SHGFI_LARGEICON returns the system large-icon metric -- 32px at 100% scaling -- no matter
    // what size is asked for, so callers wanting more than that are served from the shell's
    // extra-large (48px) or jumbo (256px) image lists instead. Below the threshold SHGetFileInfo
    // stays the cheaper path.
    private const int LargeIconMetric = 32;

    public byte[]? GetIconPng(string path, int size)
    {
        if (size > LargeIconMetric)
        {
            var scaled = TryGetImageListIconPng(path, size);
            if (scaled is not null)
                return scaled;
        }

        var flags = NativeMethods.SHGFI_ICON | (size <= 16 ? NativeMethods.SHGFI_SMALLICON : NativeMethods.SHGFI_LARGEICON);
        var info = new NativeMethods.SHFILEINFO();

        var result = NativeMethods.SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<NativeMethods.SHFILEINFO>(), flags);
        if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero)
            return null;

        try
        {
            using var icon = Icon.FromHandle(info.hIcon);
            using var bitmap = icon.ToBitmap();
            return ToPng(bitmap);
        }
        finally
        {
            NativeMethods.DestroyIcon(info.hIcon);
        }
    }

    /// <summary>
    /// Resolves an AppUserModelID through the shell's Applications folder.
    ///
    /// <c>shell:AppsFolder\&lt;id&gt;</c> is the one name that reaches every kind of app the taskbar
    /// can show a button for -- packaged, unpackaged, and the Squirrel-installed ones in between --
    /// and parsing it gives a PIDL that <c>SHGetFileInfo</c> will answer about exactly as it would
    /// a path.
    ///
    /// Some taskbar buttons carry a real executable path as their id rather than an AppUserModelID,
    /// so that is tried first: it is cheaper, and it gets the extra-large image lists that the
    /// AppsFolder route does not.
    /// </summary>
    public byte[]? GetAppIconPng(string appUserModelId, int size)
    {
        if (string.IsNullOrWhiteSpace(appUserModelId))
            return null;

        if (Path.IsPathRooted(appUserModelId) && File.Exists(appUserModelId))
            return GetIconPng(appUserModelId, size);

        var pidl = IntPtr.Zero;

        try
        {
            var parsed = NativeMethods.SHParseDisplayName(
                $@"shell:AppsFolder\{appUserModelId}", IntPtr.Zero, out pidl, 0, out _);

            if (parsed != 0 || pidl == IntPtr.Zero)
                return null;

            var info = new NativeMethods.SHFILEINFO();
            var flags = NativeMethods.SHGFI_PIDL | NativeMethods.SHGFI_ICON |
                        (size <= 16 ? NativeMethods.SHGFI_SMALLICON : NativeMethods.SHGFI_LARGEICON);

            var result = NativeMethods.SHGetFileInfoPidl(
                pidl, 0, ref info, (uint)Marshal.SizeOf<NativeMethods.SHFILEINFO>(), flags);

            if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero)
                return null;

            try
            {
                using var icon = Icon.FromHandle(info.hIcon);
                using var bitmap = icon.ToBitmap();
                return ToPng(bitmap);
            }
            finally
            {
                NativeMethods.DestroyIcon(info.hIcon);
            }
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or ExternalException)
        {
            return null;
        }
        finally
        {
            if (pidl != IntPtr.Zero)
                Marshal.FreeCoTaskMem(pidl);
        }
    }

    private static byte[]? TryGetImageListIconPng(string path, int size)
    {
        var info = new NativeMethods.SHFILEINFO();

        // SHGFI_SYSICONINDEX asks only for the index into the shared system image list, so unlike
        // the SHGFI_ICON path above it allocates no HICON for us to destroy.
        var result = NativeMethods.SHGetFileInfo(path, 0, ref info,
            (uint)Marshal.SizeOf<NativeMethods.SHFILEINFO>(), NativeMethods.SHGFI_SYSICONINDEX);
        if (result == IntPtr.Zero)
            return null;

        var whichList = size > 48 ? NativeMethods.SHIL_JUMBO : NativeMethods.SHIL_EXTRALARGE;
        var iid = NativeMethods.IID_IImageList;

        NativeMethods.IImageList? imageList = null;
        var hIcon = IntPtr.Zero;
        try
        {
            if (NativeMethods.SHGetImageList(whichList, ref iid, out imageList) != 0 || imageList is null)
                return null;

            if (imageList.GetIcon(info.iIcon, NativeMethods.ILD_TRANSPARENT, ref hIcon) != 0 || hIcon == IntPtr.Zero)
                return null;

            using var icon = Icon.FromHandle(hIcon);
            using var bitmap = icon.ToBitmap();

            // A jumbo list entry is always a 256x256 canvas, but files whose icon has no 256px
            // variant come back as a small image sitting in the corner of all that transparency.
            // Rendering that as-is would show the icon at a fraction of its intended size, so crop
            // to the opaque content and let the UI scale the real pixels.
            using var trimmed = TrimTransparentBorder(bitmap);
            return ToPng(trimmed ?? bitmap);
        }
        catch (Exception ex) when (ex is COMException or EntryPointNotFoundException or DllNotFoundException or ArgumentException)
        {
            // Falls through to the SHGetFileInfo path -- a smaller icon beats no icon.
            return null;
        }
        finally
        {
            if (hIcon != IntPtr.Zero)
                NativeMethods.DestroyIcon(hIcon);
            if (imageList is not null)
                Marshal.ReleaseComObject(imageList);
        }
    }

    /// <summary>
    /// Returns the bitmap cropped to its non-transparent bounds, or null when there is nothing to
    /// crop (fully transparent, or already tight against every edge).
    /// </summary>
    private static Bitmap? TrimTransparentBorder(Bitmap source)
    {
        if (!Image.IsAlphaPixelFormat(source.PixelFormat))
            return null;

        var bounds = new Rectangle(0, 0, source.Width, source.Height);
        var data = source.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

        int minX = source.Width, minY = source.Height, maxX = -1, maxY = -1;
        try
        {
            unsafe
            {
                for (var y = 0; y < source.Height; y++)
                {
                    var row = (byte*)data.Scan0 + (y * data.Stride);
                    for (var x = 0; x < source.Width; x++)
                    {
                        // Format32bppArgb is BGRA in memory, so alpha is the 4th byte.
                        if (row[(x * 4) + 3] == 0)
                            continue;

                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }
        }
        finally
        {
            source.UnlockBits(data);
        }

        if (maxX < 0 || maxY < 0)
            return null;

        var crop = Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
        if (crop == bounds)
            return null;

        return source.Clone(crop, PixelFormat.Format32bppArgb);
    }

    private static byte[] ToPng(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }
}
