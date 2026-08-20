using System.Collections.Specialized;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using Dock.Core.Services;

namespace Dock.Interop.Shell;

public sealed class ClipboardWriter : IClipboardWriter
{
    public void SetText(string text) => Write(() => System.Windows.Clipboard.SetText(text));

    public void SetImage(byte[] png) => Write(() =>
    {
        using var stream = new MemoryStream(png);

        var image = new BitmapImage();
        image.BeginInit();

        // OnLoad, or the BitmapImage keeps a reference to a stream this method is about to dispose
        // and the clipboard ends up holding a decoder pointed at nothing.
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();

        System.Windows.Clipboard.SetImage(image);
    });

    public void SetFiles(IReadOnlyList<string> paths) => Write(() =>
    {
        var list = new StringCollection();

        // Whatever is left of the set: files copied an hour ago get moved and deleted, and putting
        // a path that no longer resolves onto the clipboard turns the next Ctrl+V into an error
        // dialog in Explorer rather than a paste.
        foreach (var path in paths.Where(p => File.Exists(p) || Directory.Exists(p)))
            list.Add(path);

        if (list.Count > 0)
            System.Windows.Clipboard.SetFileDropList(list);
    });

    /// <summary>
    /// Every write goes through here for the same reason the first one did: the clipboard is a
    /// single system-wide resource and any app can have it open at the moment we ask.
    /// </summary>
    private static void Write(Action write)
    {
        try
        {
            write();
        }
        catch (COMException)
        {
            // Another app is transiently holding the clipboard open -- nothing useful to retry
            // synchronously here, and the user can just click the entry again.
        }
    }
}
