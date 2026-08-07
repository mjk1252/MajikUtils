using System.Windows;
using System.Windows.Controls;

namespace Dock.App.Views;

/// <summary>
/// Picks an activity's *compact* form: what it looks like beside whatever holds the pill, rather
/// than in it.
///
/// An activity has two appearances and an implicit <c>DataTemplate DataType</c> can only express
/// one. Media in the pill is a title, an artist and an equalizer; the camera indicator in the pill
/// is a dot, an icon and a name. Compacted, both are a single glyph's worth of space.
///
/// So the compact set is keyed by convention -- <c>Compact.</c> plus the view model's type name --
/// and looked up here. An activity that has not thought about its compact form still renders
/// something honest through <see cref="Fallback"/>, which is what keeps <c>IIslandActivity</c> from
/// growing a flag to declare whether it has one.
/// </summary>
public sealed class CompactActivityTemplateSelector : DataTemplateSelector
{
    public DataTemplate? Fallback { get; set; }

    public override DataTemplate? SelectTemplate(object? item, DependencyObject container)
    {
        if (item is null || container is not FrameworkElement element)
            return null;

        return element.TryFindResource($"Compact.{item.GetType().Name}") as DataTemplate ?? Fallback;
    }
}
