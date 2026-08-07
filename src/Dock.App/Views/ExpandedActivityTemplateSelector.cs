using System.Windows;
using System.Windows.Controls;

namespace Dock.App.Views;

/// <summary>
/// Picks an activity's row in the expanded panel, by the same convention the compact forms use:
/// <c>Expanded.</c> plus the view model's type name.
///
/// A missing template is not a mistake. Media is the case that proves it: its expanded form is the
/// artwork, the timeline and the transport controls, which is a block of its own further down the
/// panel rather than a row in this list.
///
/// Such an activity gets <see cref="Empty"/> rather than null, and that distinction is load-bearing.
/// Returning null does *not* mean "draw nothing" -- a ContentPresenter handed null falls back to
/// looking up an implicit <c>DataType</c> template for the item, finds the one that draws the
/// collapsed pill, and renders it here as well. The result is the now-playing row appearing above
/// every section, which is exactly what this list was meant to stop.
/// </summary>
public sealed class ExpandedActivityTemplateSelector : DataTemplateSelector
{
    /// <summary>Drawn for an activity with nothing to say in the expanded panel.</summary>
    public DataTemplate? Empty { get; set; }

    public override DataTemplate? SelectTemplate(object? item, DependencyObject container)
    {
        if (item is null || container is not FrameworkElement element)
            return Empty;

        return element.TryFindResource($"Expanded.{item.GetType().Name}") as DataTemplate ?? Empty;
    }
}
