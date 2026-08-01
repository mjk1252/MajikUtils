using System.Windows;
using System.Windows.Input;

namespace Dock.App.Views.Panels;

/// <summary>
/// Turns a press-and-move on a list row into a file drag. Shared by the Recent, Shelf and Stack
/// panels, which all present file rows that must be draggable out to other applications.
/// </summary>
public sealed class FileDragGesture
{
    /// <summary>
    /// Marks a drag as originating inside Dock, so a panel that is itself a drop target can tell
    /// its own items apart from files dragged in from Explorer.
    /// </summary>
    public const string InternalDragFormat = "Dock.InternalDrag";

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
    public bool TryDrag(MouseEventArgs e, DependencyObject source, string path)
    {
        if (e.LeftButton != MouseButtonState.Pressed || !HasExceededThreshold(e))
            return false;

        _start = null;

        var data = new DataObject(DataFormats.FileDrop, new[] { path });
        data.SetData(InternalDragFormat, true);
        DragDrop.DoDragDrop(source, data, DragDropEffects.Copy | DragDropEffects.Move);
        return true;
    }
}
