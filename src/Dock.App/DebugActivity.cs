using System.ComponentModel;
using Dock.Core.ViewModels;

namespace Dock.App;

/// <summary>
/// A stand-in activity, registered only when the app is started with <c>--debug-activity</c>.
///
/// The island can only produce two activities on its own -- something playing and the camera -- and
/// the camera needs a camera. This exists so the arbitration and the bubble can be watched without
/// one: three activities competing, a runner-up arriving and leaving, priorities crossing over.
///
/// It has no compact template of its own on purpose. That makes it the only thing that exercises
/// <c>CompactActivityFallback</c>, which is the safety net every future activity lands in before
/// anybody writes it a proper one.
/// </summary>
internal sealed class DebugActivity : IIslandActivity
{
    private bool _isActive;

    public event PropertyChangedEventHandler? PropertyChanged;

    public required string Key { get; init; }

    /// <summary>Shown by the pill when this is primary: with no DataTemplate, WPF draws ToString().</summary>
    public required string Label { get; init; }

    public IslandPriority Priority { get; init; } = IslandPriority.Background;

    /// <summary>No linger: a switch being flipped by hand is not a source that flaps.</summary>
    public TimeSpan Linger => TimeSpan.Zero;

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value)
                return;

            _isActive = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActive)));
        }
    }

    public void Retire()
    {
        // Nothing to clear -- the label is fixed.
    }

    public override string ToString() => Label;
}
