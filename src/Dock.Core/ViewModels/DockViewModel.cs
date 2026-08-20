using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Core.Models;
using Dock.Core.Services;

namespace Dock.Core.ViewModels;

public partial class DockViewModel : ObservableObject
{
    private readonly ShelfStore _shelfStore = new();
    private readonly StackStore _stackStore = new();
    private readonly IIconProvider _iconProvider;
    private readonly IAppLauncher _launcher;

    private const int MaxClipboardEntries = 25;

    /// <summary>
    /// How much of the clipboard history may be image bytes before the oldest images start being
    /// dropped.
    ///
    /// A count alone stopped being a bound the moment images were allowed in: twenty-five lines of
    /// text is a few kilobytes, twenty-five 4K screenshots is most of a gigabyte, and this history
    /// lives entirely in memory. The budget evicts by age rather than re-encoding, so what comes
    /// back out is always the pixels that went in -- the list just holds fewer big things.
    /// </summary>
    private const long MaxClipboardImageBytes = 150L * 1024 * 1024;

    private IWingetService? _wingetService;
    private IWingetProgress? _wingetProgress;
    private IClipboardWriter? _clipboardWriter;
    private readonly ClipboardStore _clipboardStore = new();
    private string _clipboardQuery = "";
    private List<AppLauncherItemViewModel> _allLauncherItems = [];
    private string _launcherQuery = "";

    public ObservableCollection<AppLauncherItemViewModel> LauncherResults { get; } = [];
    public ObservableCollection<WingetResultViewModel> WingetResults { get; } = [];
    public ObservableCollection<RecentFileItemViewModel> RecentFiles { get; } = [];
    public ObservableCollection<ShelfItemViewModel> ShelfItems { get; } = [];
    public ObservableCollection<StackItemViewModel> Stacks { get; } = [];
    /// <summary>Everything held, newest first. The source of truth; the panel draws the filtered view.</summary>
    public ObservableCollection<ClipboardEntryViewModel> ClipboardHistory { get; } = [];

    /// <summary>
    /// What the clipboard panel actually lists: <see cref="ClipboardHistory"/> through the search
    /// box. Mirrors the launcher's arrangement rather than filtering in the view, so the palette and
    /// the store keep reading the unfiltered list.
    /// </summary>
    public ObservableCollection<ClipboardEntryViewModel> ClipboardResults { get; } = [];

    [ObservableProperty]
    private bool isWingetSearching;

    [ObservableProperty]
    private double cpuPercent;

    [ObservableProperty]
    private double gpuPercent;

    public void UpdateSystemStats(double cpuPercent, double gpuPercent)
    {
        CpuPercent = cpuPercent;
        GpuPercent = gpuPercent;
    }

    public DockViewModel(IIconProvider iconProvider, IAppLauncher launcher)
    {
        _iconProvider = iconProvider;
        _launcher = launcher;

        foreach (var shelfItem in _shelfStore.Load())
            ShelfItems.Add(CreateShelfItem(shelfItem));

        foreach (var folder in _stackStore.Load())
            Stacks.Add(CreateStack(folder));
    }

    private ShelfItemViewModel CreateShelfItem(ShelfItem item) => new(item)
    {
        IconPng = _iconProvider.GetIconPng(item.Path, 32)
    };

    private StackItemViewModel CreateStack(StackFolder folder)
    {
        var vm = new StackItemViewModel(folder) { IconPng = _iconProvider.GetIconPng(folder.Path, 48) };
        vm.Refresh(_iconProvider, _launcher);
        return vm;
    }

    public void AddStack(string path)
    {
        if (!Directory.Exists(path))
            return;

        if (Stacks.Any(s => string.Equals(s.Path, path, StringComparison.OrdinalIgnoreCase)))
            return;

        var folder = new StackFolder { Id = Guid.NewGuid().ToString(), Path = path };
        Stacks.Add(CreateStack(folder));
        SaveStacks();
    }

    [RelayCommand]
    private void RemoveStack(StackItemViewModel? item)
    {
        if (item is null)
            return;

        Stacks.Remove(item);
        SaveStacks();
    }

    private void SaveStacks() => _stackStore.Save(Stacks.Select(s => s.Folder).ToList());

    public void AttachWingetService(IWingetService wingetService, IWingetProgress? progress = null)
    {
        _wingetService = wingetService;
        _wingetProgress = progress;
    }

    /// <summary>
    /// Every installed app, unfiltered. <see cref="LauncherResults"/> is already narrowed to the
    /// Launch tab's own query and capped at 60 -- the command palette runs its own ranking across
    /// every source it merges and needs the full list to rank from.
    /// </summary>
    public IReadOnlyList<AppLauncherItemViewModel> AllLauncherItems => _allLauncherItems;

    public void SetLauncherItems(List<AppLauncherItemViewModel> items)
    {
        _allLauncherItems = items;
        FilterLauncherApps(_launcherQuery);
    }

    public void FilterLauncherApps(string query)
    {
        _launcherQuery = query;
        LauncherResults.Clear();

        var matches = string.IsNullOrWhiteSpace(query)
            ? _allLauncherItems.Take(60)
            : _allLauncherItems.Where(a => a.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(60);

        foreach (var item in matches)
            LauncherResults.Add(item);
    }

    public void SetWingetResults(IReadOnlyList<WingetResult> results)
    {
        var wingetService = _wingetService;
        if (wingetService is null)
            return;

        WingetResults.Clear();
        foreach (var result in results)
            WingetResults.Add(new WingetResultViewModel(result, wingetService, _wingetProgress));

        IsWingetSearching = false;
    }

    public void BeginWingetSearch() => IsWingetSearching = true;

    public void ClearWingetResults()
    {
        WingetResults.Clear();
        IsWingetSearching = false;
    }

    /// <summary>
    /// Builds the view-model wrappers (including icon extraction) for a set of recent files.
    /// Deliberately just returns a plain list rather than touching <see cref="RecentFiles"/>
    /// directly, so callers can do the (slower, icon-extracting) work off the UI thread and
    /// only call <see cref="SetRecentFiles"/> with the finished result on the UI thread.
    /// </summary>
    public List<RecentFileItemViewModel> BuildRecentFileItems(IReadOnlyList<RecentFile> files) =>
        files.Select(f => new RecentFileItemViewModel(f, _launcher) { IconPng = _iconProvider.GetIconPng(f.Path, 32) }).ToList();

    public void SetRecentFiles(List<RecentFileItemViewModel> items)
    {
        RecentFiles.Clear();
        foreach (var item in items)
            RecentFiles.Add(item);
    }

    public void AddToShelf(string path)
    {
        var name = System.IO.Path.GetFileName(path.TrimEnd('\\', '/'));
        if (ShelfItems.Any(s => string.Equals(s.Path, path, StringComparison.OrdinalIgnoreCase)))
            return;

        var item = new ShelfItem { Name = name, Path = path };
        ShelfItems.Add(CreateShelfItem(item));
        SaveShelf();
    }

    [RelayCommand]
    private void RemoveFromShelf(ShelfItemViewModel? item)
    {
        if (item is null)
            return;

        ShelfItems.Remove(item);
        SaveShelf();
    }

    private void SaveShelf() => _shelfStore.Save(ShelfItems.Select(s => s.Item).ToList());

    /// <summary>
    /// Hands over the writer, and restores whatever was pinned last session.
    ///
    /// The restore lives here rather than in the constructor because a pinned entry needs a writer
    /// to be worth building -- clicking one has to put it back on the clipboard, and a row that
    /// cannot do that is decoration.
    /// </summary>
    public void AttachClipboardWriter(IClipboardWriter writer)
    {
        _clipboardWriter = writer;

        foreach (var entry in _clipboardStore.Load().OrderByDescending(e => e.CapturedAt))
            ClipboardHistory.Add(new ClipboardEntryViewModel(entry, writer, SavePinned, isPinned: true));

        FilterClipboard(_clipboardQuery);
    }

    /// <summary>
    /// Narrows the panel's list. Searches the preview rather than the entry, so a file entry
    /// matches on its names and an image on its dimensions -- which is all either of them has in
    /// the way of words.
    /// </summary>
    public void FilterClipboard(string query)
    {
        _clipboardQuery = query;
        ClipboardResults.Clear();

        var matches = string.IsNullOrWhiteSpace(query)
            ? ClipboardHistory
            : ClipboardHistory.Where(e => e.Matches(query));

        foreach (var entry in matches)
            ClipboardResults.Add(entry);
    }

    private void SavePinned()
    {
        _clipboardStore.Save(ClipboardHistory.Where(e => e.IsPinned).Select(e => e.Entry));
        FilterClipboard(_clipboardQuery);
    }

    /// <summary>Convenience for the commonest kind, and what the tests mostly reach for.</summary>
    public void AddClipboardEntry(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        AddClipboardEntry(ClipboardEntry.ForText(text, DateTime.Now));
    }

    /// <summary>
    /// Records something that was copied, whatever it was.
    ///
    /// Two bounds, because they answer different questions. The count keeps the list a list -- a
    /// panel is not a database browser. The byte budget keeps a history of screenshots from being
    /// the largest thing in the process; text entries do not count towards it, so a run of ordinary
    /// copies can never evict anything.
    /// </summary>
    public void AddClipboardEntry(ClipboardEntry entry)
    {
        if (_clipboardWriter is not { } writer)
            return;

        // Copying an entry back out writes it to the real clipboard too, which re-triggers the
        // capture path -- without this check that would push a duplicate right back onto the
        // top of the list it was just selected from.
        if (ClipboardHistory.Count > 0 && ClipboardHistory[0].Entry.Signature == entry.Signature)
            return;

        ClipboardHistory.Insert(0, new ClipboardEntryViewModel(entry, writer, SavePinned));

        AttachIcons(ClipboardHistory[0]);
        TrimHistory();
        FilterClipboard(_clipboardQuery);
    }

    /// <summary>
    /// Applies both bounds. Pinned entries are exempt from each of them: the count is a limit on
    /// how much is being kept *for* you, and a pin moves an entry out of that pile and into the one
    /// you asked for.
    /// </summary>
    private void TrimHistory()
    {
        for (var i = ClipboardHistory.Count - 1; i > 0 && UnpinnedCount() > MaxClipboardEntries; i--)
        {
            if (!ClipboardHistory[i].IsPinned)
                ClipboardHistory.RemoveAt(i);
        }

        // Oldest first, never the entry just added, never a pin -- see ClipboardBudget for why
        // those exceptions are the whole point of the rule.
        var costs = ClipboardHistory
            .Select(e => new ClipboardCost(e.Entry.ByteCost, e.IsPinned))
            .ToList();

        // Descending, so removing one does not shift the index of the next.
        foreach (var index in ClipboardBudget.Excess(costs, MaxClipboardImageBytes).OrderByDescending(i => i))
            ClipboardHistory.RemoveAt(index);
    }

    private int UnpinnedCount() => ClipboardHistory.Count(e => !e.IsPinned);

    /// <summary>
    /// Fills in the shell icons for a files entry, the same way the shelf does. Best effort: a path
    /// that has already been moved or deleted simply has no icon, and the row still names it.
    /// </summary>
    private void AttachIcons(ClipboardEntryViewModel entry)
    {
        foreach (var file in entry.Files)
            file.IconPng = _iconProvider.GetIconPng(file.Path, 16);
    }

    /// <summary>
    /// Empties the history, except for what was pinned. "Clear" means "throw away what I did not
    /// ask you to keep" -- a pin that a neighbouring button could wipe out is not a pin.
    /// </summary>
    [RelayCommand]
    private void ClearClipboardHistory()
    {
        for (var i = ClipboardHistory.Count - 1; i >= 0; i--)
        {
            if (!ClipboardHistory[i].IsPinned)
                ClipboardHistory.RemoveAt(i);
        }

        FilterClipboard(_clipboardQuery);
    }
}
