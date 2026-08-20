using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Dock.Core.ViewModels;

namespace Dock.App.Views.Panels;

/// <summary>
/// The command palette, living in the island instead of in a window of its own.
///
/// What was deleted along with that window: a second search box, a second set of arrow-key
/// handling, a second placement routine, and the answer to "which of these two things do I press".
/// What survives untouched is <see cref="CommandPaletteViewModel"/> -- the ranking and the merge
/// across apps, stacks, recent files and clipboard history were never the part that wanted a window.
///
/// The keyboard is driven from outside. The caret stays in the island's capture box while the
/// arrows move a selection down here, which is how every search box worth using behaves and is why
/// nothing in this control takes focus.
/// </summary>
public partial class SearchPanel : UserControl
{
    /// <summary>Raised once a result has been activated, so the host can put the island away.</summary>
    public event Action? Activated;

    public SearchPanel()
    {
        InitializeComponent();
    }

    private CommandPaletteViewModel? ViewModel => DataContext as CommandPaletteViewModel;

    /// <summary>
    /// Re-ranks against a new query. Called on every keystroke in the capture box rather than bound
    /// to it, because the box's text is the *whole* line including the leading slash and this wants
    /// only what follows it.
    /// </summary>
    public void Search(string query)
    {
        if (ViewModel is not { } viewModel)
            return;

        viewModel.Query = query;

        // Selection is reset rather than clamped: after a new keystroke the old highlighted row is
        // rarely the same row, and leaving the highlight where it sat means Enter runs whatever
        // happens to have shuffled into that position.
        ResultsList.SelectedIndex = ResultsList.Items.Count > 0 ? 0 : -1;
    }

    /// <summary>Moves the highlight, and reports whether there was anything to move.</summary>
    public bool MoveSelection(int delta)
    {
        if (ResultsList.Items.Count == 0)
            return false;

        ResultsList.SelectedIndex = Math.Clamp(
            ResultsList.SelectedIndex + delta, 0, ResultsList.Items.Count - 1);

        ResultsList.ScrollIntoView(ResultsList.SelectedItem);
        return true;
    }

    /// <summary>
    /// Runs the highlighted result, or the top one if the arrows were never touched -- typing three
    /// letters and pressing Enter has to work without a trip through the list.
    /// </summary>
    public bool ActivateSelected()
    {
        var item = ResultsList.SelectedItem as PaletteItemViewModel
            ?? ResultsList.Items.Cast<PaletteItemViewModel>().FirstOrDefault();

        return Activate(item);
    }

    private void OnResultClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PaletteItemViewModel item })
            Activate(item);
    }

    private bool Activate(PaletteItemViewModel? item)
    {
        if (item is null || !item.ActivateCommand.CanExecute(null))
            return false;

        // The island goes away first. Launching an app takes the foreground, and an overlay still
        // sitting on top of the thing it just opened is the wrong last frame.
        Activated?.Invoke();
        item.ActivateCommand.Execute(null);
        return true;
    }
}
