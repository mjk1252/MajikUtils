using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Dock.Core.Models;
using Dock.Core.Services;

namespace Dock.Core.ViewModels;

/// <summary>
/// Which application is using the camera, on the island.
///
/// The macOS dot, in the place macOS puts it. Windows has the data and shows it only as a tray
/// glyph with no name attached to it, so the useful half -- *which* application -- is the part
/// worth surfacing.
/// </summary>
public sealed partial class PrivacyViewModel : ObservableObject, IIslandActivity
{
    /// <summary>
    /// Long enough to ride out an application dropping the camera for a moment mid-call, which
    /// they do when renegotiating a stream or switching device. Without this the island would
    /// flicker.
    /// </summary>
    private static readonly TimeSpan UsageLinger = TimeSpan.FromSeconds(2);

    private readonly IIconProvider _icons;

    /// <summary>Something has the camera right now. The claim, which starts the host's linger.</summary>
    [ObservableProperty] private bool _isActive;

    // Display state, both of them. Held across the linger window and cleared only by Retire, or the
    // pill would blank itself every time an application let go of the camera for a moment.
    [ObservableProperty] private bool _cameraInUse;
    [ObservableProperty] private string _summary = string.Empty;
    [ObservableProperty] private byte[]? _icon;

    public ObservableCollection<DeviceUsageItemViewModel> Apps { get; } = [];

    public string Key => "privacy";

    /// <summary>
    /// Below music, because a dot says everything this has to say.
    ///
    /// "The camera is on" is worth knowing at a glance and is not worth reading a sentence about,
    /// so taking the whole pill off a playing track to spell it out trades something the user
    /// chose for something they did not ask for. At this rank it compacts to a dot beside the
    /// track instead -- which is exactly what macOS does with the same information.
    ///
    /// With nothing playing it takes the pill on its own and names the application, because then
    /// there is nothing to take it from and the room is free.
    /// </summary>
    public IslandPriority Priority => IslandPriority.Background;

    public TimeSpan Linger => UsageLinger;

    public PrivacyViewModel(IIconProvider icons)
    {
        _icons = icons;
    }

    /// <summary>
    /// Takes a reading. An empty one drops the claim and leaves everything else standing -- see
    /// <see cref="Retire"/>, which is where a camera that stayed released actually clears.
    /// </summary>
    public void Apply(IReadOnlyList<DeviceUsage> usages)
    {
        IsActive = usages.Count > 0;

        if (usages.Count == 0)
            return;

        CameraInUse = true;
        RefreshApps(usages);
        Summary = BuildSummary();
    }

    public void Retire()
    {
        CameraInUse = false;
        Summary = string.Empty;
        Icon = null;
        Apps.Clear();
    }

    /// <summary>
    /// Rebuilds the list only where it actually differs. The registry republishes the whole set on
    /// every change, and rebuilding a collection the island is drawing would restart the icons and
    /// blink the rows for a reading that named the same application as the one before it.
    /// </summary>
    private void RefreshApps(IReadOnlyList<DeviceUsage> usages)
    {
        var incoming = usages
            .OrderBy(u => u.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (incoming.Count == Apps.Count
            && incoming.Zip(Apps).All(p => p.First.DisplayName == p.Second.Name))
        {
            return;
        }

        Apps.Clear();

        foreach (var usage in incoming)
        {
            Apps.Add(new DeviceUsageItemViewModel(usage.DisplayName)
            {
                IconPng = usage.AppPath.Length > 0 ? _icons.GetIconPng(usage.AppPath, 32) : null
            });
        }

        // The collapsed pill has room for one, and the one that matters is whoever is at the top
        // of the list the expanded panel shows.
        Icon = Apps.Count > 0 ? Apps[0].IconPng : null;
    }

    /// <summary>
    /// What the collapsed pill says. One application is named; several are counted, because three
    /// names do not fit and a count is the honest summary of them.
    /// </summary>
    private string BuildSummary() => Apps.Count switch
    {
        0 => "Camera in use",
        1 => $"{Apps[0].Name} · camera",
        _ => $"{Apps.Count} apps · camera"
    };
}

/// <summary>One application in the expanded list.</summary>
public sealed class DeviceUsageItemViewModel(string name)
{
    public string Name { get; } = name;

    public byte[]? IconPng { get; init; }
}
