using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Core.Services;

namespace Dock.Core.ViewModels;

/// <summary>
/// One row in the mixer. Unlike most of the read-only view models here, its <see cref="Volume"/>
/// and <see cref="IsMuted"/> are two-way: dragging the slider writes back through
/// <see cref="IVolumeMixerSource"/> immediately, the same directness the transport buttons use to
/// reach the media session.
/// </summary>
public sealed partial class AudioSessionViewModel : ObservableObject
{
    private readonly IVolumeMixerSource _source;
    private bool _applyingExternalChange;

    public int ProcessId { get; }
    public string Name { get; }

    [ObservableProperty] private byte[]? _iconPng;
    [ObservableProperty] private double _volume;
    [ObservableProperty] private bool _isMuted;

    public AudioSessionViewModel(int processId, string name, IVolumeMixerSource source)
    {
        ProcessId = processId;
        Name = name;
        _source = source;
    }

    /// <summary>
    /// Brings the two display fields in from a fresh reading, without touching <see cref="Volume"/>
    /// or <see cref="IsMuted"/> through the same path a drag would use -- that would immediately
    /// write the just-read value straight back to Core Audio, which is harmless but pointless.
    /// </summary>
    public void ApplyReading(double volume, bool isMuted)
    {
        _applyingExternalChange = true;
        Volume = volume;
        IsMuted = isMuted;
        _applyingExternalChange = false;
    }

    partial void OnVolumeChanged(double value)
    {
        if (!_applyingExternalChange)
            _source.SetVolume(ProcessId, value);
    }

    partial void OnIsMutedChanged(bool value)
    {
        if (!_applyingExternalChange)
            _source.SetMuted(ProcessId, value);
    }

    [RelayCommand]
    private void ToggleMute() => IsMuted = !IsMuted;
}
