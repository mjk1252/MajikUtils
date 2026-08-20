using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace Dock.App.Views.Panels;

/// <summary>
/// Turns a press-and-move on a list row into a file drag. Shared by the Recent, Shelf and Stack
/// panels, which all present file rows that must be draggable out to other applications.
/// </summary>
public sealed class FileDragGesture
{
    /// <summary>
    /// Marks a drag as originating inside MajikUtils, so a panel that is itself a drop target can tell
    /// its own items apart from files dragged in from Explorer.
    /// </summary>
    public const string InternalDragFormat = "MajikUtils.InternalDrag";

    private Point? _start;

    public void Begin(MouseButtonEventArgs e) => _start = e.GetPosition(null);

    /// <summary>
    /// True once the cursor has moved past WPF's standard drag threshold from the button-down
    /// point -- without this, ANY mouse move while the button happens to be held (a few pixels
    /// of hand tremor between mouse-down and mouse-up is unavoidable on basically every click)
    /// would start a drag, and that blocking DragDrop.DoDragDrop call swallows the mouse-up the
    /// row's own click handler is waiting for.
    /// </summary>
    private bool HasExceededThreshold(MouseEventArgs e)
    {
        if (_start is not { } start)
            return false;

        var current = e.GetPosition(null);
        return Math.Abs(current.X - start.X) >= SystemParameters.MinimumHorizontalDragDistance ||
               Math.Abs(current.Y - start.Y) >= SystemParameters.MinimumVerticalDragDistance;
    }

    /// <summary>
    /// Starts a file drag for <paramref name="path"/> if the gesture qualifies. Returns false
    /// when the move was just tremor, so callers can treat the interaction as a plain click.
    /// </summary>
    public bool TryDrag(MouseEventArgs e, DependencyObject source, string path) =>
        TryDrag(e, source, ForFiles([path]));

    /// <summary>
    /// The same gesture for a drag that is not a single file.
    ///
    /// The clipboard history needed this: an entry there can be a picture or a line of text as
    /// easily as a path, and all three are worth being able to pull out of the island. Only what is
    /// being carried differs -- the threshold, the internal marker and the blocking DoDragDrop call
    /// are the parts nobody should be writing twice.
    /// </summary>
    public bool TryDrag(MouseEventArgs e, DependencyObject source, DataObject data)
    {
        if (e.LeftButton != MouseButtonState.Pressed || !HasExceededThreshold(e))
            return false;

        _start = null;

        data.SetData(InternalDragFormat, true);
        DragDrop.DoDragDrop(source, data, DragDropEffects.Copy | DragDropEffects.Move);
        return true;
    }

    public static DataObject ForFiles(IEnumerable<string> paths) =>
        new(DataFormats.FileDrop, paths.ToArray());

    public static DataObject ForText(string text) => new(DataFormats.UnicodeText, text);

    /// <summary>
    /// A picture, offered both ways at once: as the file it was just written to, and as a bitmap
    /// for the applications that would rather have the pixels than a path.
    /// </summary>
    public static DataObject ForImage(string path, byte[] png)
    {
        var data = ForFiles([path]);

        try
        {
            using var stream = new MemoryStream(png);

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();

            data.SetImage(image);
        }
        catch (Exception)
        {
            // The file half of the drop still works, which is the half that reaches a folder.
        }

        return data;
    }
}
