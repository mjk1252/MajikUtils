using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Dock.Core.Models;
using Dock.Core.Services;

namespace Dock.Core.ViewModels;

public partial class StackItemViewModel : ObservableObject
{
    // Above this many entries the arc gets too crowded to read -- shown items are capped and the
    // rest collapse into a single "+N more" entry that opens the real folder instead.
    private const int MaxFanEntries = 8;

    public StackFolder Folder { get; }
    public string Path => Folder.Path;
    public string Name => System.IO.Path.GetFileName(Folder.Path.TrimEnd('\\', '/'));

    [ObservableProperty]
    private byte[]? iconPng;

    public ObservableCollection<StackEntryViewModel> Entries { get; } = [];

    public StackItemViewModel(StackFolder folder)
    {
        Folder = folder;
    }

    public void Refresh(IIconProvider iconProvider, IAppLauncher launcher)
    {
        Entries.Clear();

        if (!Directory.Exists(Path))
            return;

        List<string> paths;
        try
        {
            paths = Directory.EnumerateFileSystemEntries(Path)
                .OrderByDescending(GetLastWriteTimeSafe)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        var shown = paths.Take(MaxFanEntries).ToList();
        foreach (var entryPath in shown)
        {
            Entries.Add(new StackEntryViewModel(entryPath, Directory.Exists(entryPath), launcher)
            {
                // Fan entries render their icon far larger than any other dock surface, so ask for
                // real pixels at that size rather than letting WPF upscale a 32px shell icon.
                IconPng = iconProvider.GetIconPng(entryPath, 96)
            });
        }
    }

    private static DateTime GetLastWriteTimeSafe(string path)
    {
        try
        {
            return Directory.Exists(path) ? Directory.GetLastWriteTime(path) : File.GetLastWriteTime(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return DateTime.MinValue;
        }
    }
}
