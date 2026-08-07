namespace Dock.Core.Services;

/// <summary>
/// The machine's power state, on the machines that have one.
///
/// <see cref="IsPresent"/> is the whole reason this interface has a property on it rather than just
/// an event: on a desktop there is nothing to report, ever, and the island should not carry a
/// battery activity that can never light up. The App asks first and only registers if the answer
/// is yes.
/// </summary>
public interface IBatterySource
{
    /// <summary>Whether this machine has a battery at all. Read once, at startup.</summary>
    bool IsPresent { get; }

    /// <summary>Raised only when a reading differs from the one before it.</summary>
    event EventHandler<BatteryStatus>? Changed;

    void Start();
    void Stop();
}

/// <param name="PercentRemaining">0 to 100, or null where Windows will not say.</param>
/// <param name="Remaining">Estimated time left on battery, or null while charging or unknown.</param>
public readonly record struct BatteryStatus(
    bool IsCharging,
    int? PercentRemaining,
    TimeSpan? Remaining);
