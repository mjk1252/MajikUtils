using System.Runtime.InteropServices;
using Dock.Core.Services;

namespace Dock.Interop.Shell;

public sealed class ClipboardWriter : IClipboardWriter
{
    public void SetText(string text)
    {
        try
        {
            System.Windows.Clipboard.SetText(text);
        }
        catch (COMException)
        {
            // Another app is transiently holding the clipboard open -- nothing useful to retry
            // synchronously here, and the user can just click the entry again.
        }
    }
}
