using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Dock.Core.ViewModels;

/// <summary>
/// One search box over everything Dock already knows how to find: installed apps, folder stacks,
/// recent files, clipboard history. Each source already has its own tab; this is not a
/// replacement for any of them, it is the "I don't remember which tab" answer -- a ranking and
/// merge layer over data the four panels already hold, not a fifth place that fetches its own.
///
/// Winget search is deliberately not one of the sources merged in here: it is a live network
/// search with its own debounce and progress state, and folding it into a list that is re-ranked
/// on every keystroke would mean either firing a web request per keystroke or reimplementing that
/// debounce a second time. The Launch tab remains the way to install something new.
/// </summary>
public sealed partial class CommandPaletteViewModel : ObservableObject
{
    /// <summary>More than a screen can usefully show at once, in any of the categories.</summary>
    private const int MaxResults = 24;

    private readonly DockViewModel _dock;

    [ObservableProperty] private string _query = string.Empty;

    public ObservableCollection<PaletteItemViewModel> Results { get; } = [];

    /// <summary>
    /// Raised when a stack row is activated. Opening a stack means showing its taskbar window,
    /// which this class -- like every other view model here -- has no reference to; the palette's
    /// host window owns the one that does.
    /// </summary>
    public event Action<StackItemViewModel>? StackActivationRequested;

    public CommandPaletteViewModel(DockViewModel dock)
    {
        _dock = dock;
    }

    partial void OnQueryChanged(string value) => Refresh();

    /// <summary>
    /// Re-ranks against whatever the four sources currently hold. Exposed rather than purely
    /// reactive to <see cref="Query"/> alone, so the window can call it on every open -- a
    /// clipboard entry copied since the palette was last shown should be searchable the moment it
    /// reopens, not only after the next keystroke.
    /// </summary>
    public void Refresh()
    {
        Results.Clear();

        var query = Query.Trim();
        if (query.Length == 0)
            return;

        var ranked = new List<(int MatchWeight, int CategoryWeight, string SortKey, PaletteItemViewModel Item)>();

        void Consider(string matchText, int categoryWeight, Func<PaletteItemViewModel> build)
        {
            if (MatchWeight(matchText, query) is { } weight)
                ranked.Add((weight, categoryWeight, matchText, build()));
        }

        foreach (var app in _dock.AllLauncherItems)
        {
            Consider(app.Name, 0, () =>
                new PaletteItemViewModel(app.Name, "App", app.IconPng, "App", app.LaunchCommand));
        }

        foreach (var stack in _dock.Stacks)
        {
            Consider(stack.Name, 1, () =>
                new PaletteItemViewModel(stack.Name, "Folder stack", stack.IconPng, "Stack",
                    new RelayCommand(() => StackActivationRequested?.Invoke(stack))));
        }

        foreach (var file in _dock.RecentFiles)
        {
            Consider(file.Name, 2, () =>
                new PaletteItemViewModel(file.Name, "Recent file", file.IconPng, "Recent", file.OpenCommand));
        }

        foreach (var entry in _dock.ClipboardHistory)
        {
            Consider(entry.Preview, 3, () =>
                new PaletteItemViewModel(entry.Preview, "Clipboard", null, "Clipboard", entry.CopyCommand));
        }

        foreach (var row in ranked
                     .OrderBy(r => r.MatchWeight)
                     .ThenBy(r => r.CategoryWeight)
                     .ThenBy(r => r.SortKey, StringComparer.OrdinalIgnoreCase)
                     .Take(MaxResults))
        {
            Results.Add(row.Item);
        }
    }

    /// <summary>Lower is a better match: a name that starts with the query beats one that merely contains it.</summary>
    private static int? MatchWeight(string text, string query)
    {
        if (text.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            return 0;

        return text.Contains(query, StringComparison.OrdinalIgnoreCase) ? 1 : null;
    }
}
