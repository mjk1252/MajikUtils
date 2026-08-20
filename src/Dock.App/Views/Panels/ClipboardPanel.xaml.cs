using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Input;
using Dock.Core.Models;
using Dock.Core.ViewModels;

namespace Dock.App.Views.Panels;

public partial class ClipboardPanel : UserControl
{
    private readonly FileDragGesture _drag = new();

    public ClipboardPanel()
    {
        InitializeComponent();
    }

    private DockViewModel ViewModel => (DockViewModel)DataContext;

    private void OnSearchChanged(object sender, TextChangedEventArgs e) =>
        ViewModel.FilterClipboard(SearchBox.Text);

    /// <summary>
    /// Puts the caret in this panel's own box. The island hides its global one while this scope is
    /// open -- two search fields on screen at once is one too many -- so somebody arriving here
    /// with the keyboard has to land somewhere, and this is the only field left.
    /// </summary>
    public void FocusSearch()
    {
        SearchBox.SelectAll();
        Keyboard.Focus(SearchBox);
    }

    /// <summary>
    /// Clicking the row copies the entry back. Clicking the pin inside it does not.
    ///
    /// The pin used to say so by marking PreviewMouseLeftButtonUp handled, which did stop the copy
    /// -- and also stopped the pin. Handling a preview event suppresses the bubbling one entirely,
    /// and the bubbling MouseLeftButtonUp is what ButtonBase raises Click from, so the toggle never
    /// fired at all. Asking where the click came from leaves the button's own handling alone.
    /// </summary>
    private void OnItemClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ClipboardEntryViewModel item })
            return;

        if (CameFromAToggle(e.OriginalSource as DependencyObject))
            return;

        item.CopyCommand.Execute(null);
    }

    private static bool CameFromAToggle(DependencyObject? source)
    {
        for (var node = source; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is ToggleButton)
                return true;
        }

        return false;
    }

    private void OnClearClick(object sender, MouseButtonEventArgs e)
    {
        ViewModel.ClearClipboardHistoryCommand.Execute(null);
        SearchBox.Clear();
        e.Handled = true;
    }

    private void OnRowMouseDown(object sender, MouseButtonEventArgs e) => _drag.Begin(e);

    /// <summary>
    /// Drags an entry out of the island and into whatever is underneath.
    ///
    /// The point of it is the screenshot: the history is where a picture you can no longer get back
    /// ends up, and until now the only way out was onto the clipboard and then into something that
    /// accepts a paste. Dragging reaches everything that accepts a drop instead, which is most of
    /// what a picture is ever wanted by.
    /// </summary>
    private void OnRowMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ClipboardEntryViewModel item } element)
            return;

        var data = BuildDragData(item);
        if (data is not null)
            _drag.TryDrag(e, element, data);
    }

    private static DataObject? BuildDragData(ClipboardEntryViewModel item) => item.Kind switch
    {
        ClipboardKind.Files => FileDragGesture.ForFiles(item.Entry.Paths),
        ClipboardKind.Image => ForImage(item.Entry),
        _ => FileDragGesture.ForText(item.Entry.Text)
    };

    /// <summary>
    /// An image goes out as a real file as well as a bitmap.
    ///
    /// Applications are split on which they accept -- a chat window takes the bitmap, a folder
    /// takes the file -- so the drop carries both and lets the target choose. The file has to exist
    /// somewhere for the second of those, hence the temp copy; it is named after the entry so
    /// dragging the same screenshot twice reuses one file rather than littering.
    /// </summary>
    private static DataObject? ForImage(ClipboardEntry entry)
    {
        if (entry.ImagePng is not { Length: > 0 } png)
            return null;

        try
        {
            var folder = Path.Combine(Path.GetTempPath(), "MajikUtils", "clipboard");
            Directory.CreateDirectory(folder);

            // The signature already identifies the bytes, and is short and filename-safe.
            var file = Path.Combine(folder, $"{entry.Signature.Replace(':', '-')}.png");

            if (!File.Exists(file))
                File.WriteAllBytes(file, png);

            return FileDragGesture.ForImage(file, png);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // No temp file means no drag. The entry can still be clicked, which is the path that
            // never touches the disk.
            return null;
        }
    }
}
