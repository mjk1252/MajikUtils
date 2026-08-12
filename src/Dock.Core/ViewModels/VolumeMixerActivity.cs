using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Dock.Core.Models;
using Dock.Core.Services;

namespace Dock.Core.ViewModels;

/// <summary>
/// Which applications are making sound, and the island's route to turning any of them down.
///
/// <see cref="Sessions"/> is not this activity's display state the way <c>MediaViewModel.Title</c>
/// is -- it is the Mixer tab's data, kept current on every reading regardless of whether anything
/// is claiming the pill, because someone opening that tab by hand to mute a stray notification
/// sound wants it to work whether or not the island happens to be showing anybody's name at that
/// moment. <see cref="Retire"/> only clears <see cref="Summary"/>, the one piece that actually is
/// pill display state.
/// </summary>
public sealed partial class VolumeMixerActivity : ObservableObject, IIslandActivity
{
    private static readonly TimeSpan ActivityLinger = TimeSpan.FromSeconds(1.5);

    /// <summary>Below this a session reads as silent even if Windows still calls it Active.</summary>
    private const double SilentVolume = 0.02;

    private readonly IIconProvider _icons;
    private readonly IVolumeMixerSource _source;

    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private string _summary = string.Empty;
    [ObservableProperty] private byte[]? _icon;

    /// <summary>
    /// Whether an application making sound is allowed to interrupt the ambient pill on its own.
    /// The Mixer tab and <see cref="Sessions"/> work regardless -- this only gates
    /// <see cref="IsActive"/>, which is the one thing Settings can switch off.
    /// </summary>
    public bool AllowPillClaim { get; set; } = true;

    public ObservableCollection<AudioSessionViewModel> Sessions { get; } = [];

    public string Key => "mixer";

    /// <summary>
    /// Below music for the same reason the camera dot is: an application making noise in the
    /// background is worth a glance, never worth replacing a track the user chose to have on
    /// screen. With nothing playing it takes the pill on its own, named, because the room is free.
    /// </summary>
    public IslandPriority Priority => IslandPriority.Background;

    public TimeSpan Linger => ActivityLinger;

    public VolumeMixerActivity(IIconProvider icons, IVolumeMixerSource source)
    {
        _icons = icons;
        _source = source;
    }

    /// <summary>
    /// Takes a fresh reading. Called every poll regardless of whether anything is audible, which is
    /// what keeps the Mixer tab live even while this activity holds neither slot.
    /// </summary>
    public void Apply(IReadOnlyList<AudioSessionInfo> sessions)
    {
        RefreshSessions(sessions);

        var loud = sessions.Where(s => s.IsActive && s.Volume > SilentVolume && !s.IsMuted).ToList();

        IsActive = AllowPillClaim && loud.Count > 0;

        if (!IsActive)
            return;

        Summary = BuildSummary(loud);
        Icon = Sessions.FirstOrDefault(s => s.ProcessId == loud[0].ProcessId)?.IconPng;
    }

    public void Retire()
    {
        Summary = string.Empty;
        Icon = null;
    }

    /// <summary>
    /// Brings <see cref="Sessions"/> in line with the reading, touching existing rows in place
    /// rather than rebuilding: a slider mid-drag would otherwise be yanked back to wherever the
    /// next poll caught it, and every icon would decode again for a set of processes that mostly
    /// did not change.
    /// </summary>
    private void RefreshSessions(IReadOnlyList<AudioSessionInfo> sessions)
    {
        var incoming = sessions.ToDictionary(s => s.ProcessId);

        for (var i = Sessions.Count - 1; i >= 0; i--)
        {
            if (!incoming.ContainsKey(Sessions[i].ProcessId))
                Sessions.RemoveAt(i);
        }

        foreach (var info in sessions.OrderBy(s => s.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            var existing = Sessions.FirstOrDefault(s => s.ProcessId == info.ProcessId);

            if (existing is not null)
            {
                existing.ApplyReading(info.Volume, info.IsMuted);
                continue;
            }

            var session = new AudioSessionViewModel(info.ProcessId, info.DisplayName, _source)
            {
                IconPng = info.ExecutablePath.Length > 0 ? _icons.GetIconPng(info.ExecutablePath, 32) : null
            };
            session.ApplyReading(info.Volume, info.IsMuted);
            Sessions.Add(session);
        }
    }

    private static string BuildSummary(List<AudioSessionInfo> loud) => loud.Count switch
    {
        1 => $"{loud[0].DisplayName} · {loud[0].Volume * 100:0}%",
        _ => $"{loud.Count} apps playing"
    };
}
