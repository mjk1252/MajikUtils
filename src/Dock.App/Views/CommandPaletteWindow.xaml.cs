using System.Windows;
using System.Windows.Input;
using Dock.Core.ViewModels;
using Dock.Interop.Windowing;

namespace Dock.App.Views;

/// <summary>
/// The Ctrl+Alt+Space surface: one box over apps, stacks, recent files and clipboard history.
///
/// Built once and reused rather than created per open (Show/Hide, not Close) -- the same reasoning
/// <see cref="PanelWindow"/> follows, minus the taskbar button, since nothing here needs one.
/// </summary>
public partial class CommandPaletteWindow : Window
{
    private readonly CommandPaletteViewModel _viewModel;

    public CommandPaletteWindow(CommandPaletteViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
    }

    /// <summary>
    /// Opens fresh every time: the query clears and the ranking runs again, so a clipboard entry
    /// copied since the palette was last used is searchable immediately rather than after the next
    /// keystroke.
    /// </summary>
    public void ShowAndFocus()
    {
        _viewModel.Query = string.Empty;
        _viewModel.Refresh();

        PositionOnCursorMonitor();

        Show();
        Activate();
        Keyboard.Focus(QueryInput);
    }

    /// <summary>
    /// Centred on whichever monitor the pointer is on, a little above the vertical middle -- the
    /// same placement every spotlight-style launcher uses, because a box exactly centred reads as
    /// a dialog box rather than something summoned to where attention already is.
    ///
    /// In DIPs rather than through <see cref="MonitorPlacement.SetPhysicalBounds"/>: this window
    /// never spans two monitors and is not pinned to a screen edge, so the physical-pixel care the
    /// island takes buys nothing here that dividing by the one monitor's own scale does not.
    /// </summary>
    private void PositionOnCursorMonitor()
    {
        var work = MonitorPlacement.FromCursor();
        var scale = work.Scale <= 0 ? 1.0 : work.Scale;

        var leftPhysical = work.Left + (work.Width - Width * scale) / 2;
        var topPhysical = work.Top + work.Height * 0.22;

        Left = leftPhysical / scale;
        Top = topPhysical / scale;
    }

    /// <summary>Losing the foreground is how a spotlight-style window is meant to go away.</summary>
    private void OnDeactivated(object? sender, EventArgs e) => Hide();

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Hide();
                e.Handled = true;
                break;

            case Key.Enter:
                ActivateSelected();
                e.Handled = true;
                break;

            case Key.Down:
                MoveSelection(1);
                e.Handled = true;
                break;

            case Key.Up:
                MoveSelection(-1);
                e.Handled = true;
                break;
        }
    }

    private void MoveSelection(int delta)
    {
        if (ResultsList.Items.Count == 0)
            return;

        var next = Math.Clamp(ResultsList.SelectedIndex + delta, 0, ResultsList.Items.Count - 1);
        ResultsList.SelectedIndex = next;
        ResultsList.ScrollIntoView(ResultsList.SelectedItem);
    }

    /// <summary>Enter activates the highlighted row, or the top one if arrow keys were never touched.</summary>
    private void ActivateSelected()
    {
        var item = ResultsList.SelectedItem as PaletteItemViewModel
            ?? ResultsList.Items.Cast<PaletteItemViewModel>().FirstOrDefault();

        ActivateItem(item);
    }

    private void OnResultClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PaletteItemViewModel item })
            ActivateItem(item);
    }

    private void ActivateItem(PaletteItemViewModel? item)
    {
        if (item is null)
            return;

        Hide();

        if (item.ActivateCommand.CanExecute(null))
            item.ActivateCommand.Execute(null);
    }
}
