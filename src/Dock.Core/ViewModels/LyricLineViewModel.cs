using CommunityToolkit.Mvvm.ComponentModel;

namespace Dock.Core.ViewModels;

/// <summary>
/// One row in the lyrics list. <see cref="IsCurrent"/> is state <c>MediaViewModel</c> flips
/// directly on the item that has it, rather than something the view works out from an index --
/// binding a highlight to "am I the Nth item" needs a converter and a container-generation trick
/// for no real benefit when the view model can just say which one it means.
/// </summary>
public sealed partial class LyricLineViewModel(string text) : ObservableObject
{
    public string Text { get; } = text;

    [ObservableProperty] private bool _isCurrent;
}
