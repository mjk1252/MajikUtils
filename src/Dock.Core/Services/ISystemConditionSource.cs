namespace Dock.Core.Services;

/// <summary>
/// The standing conditions the island shows as a dot: focus mode being on, a restart being pending.
///
/// Polled rather than pushed. Neither has a notification worth waiting on, both change on the order
/// of minutes, and a poll that costs a shell call and a registry probe is cheaper than the
/// machinery to be told.
/// </summary>
public interface ISystemConditionSource
{
    /// <summary>Raised only when a reading differs from the one before it.</summary>
    event EventHandler<SystemConditions>? Changed;

    void Start();
    void Stop();
}

/// <param name="DoNotDisturb">Focus assist, quiet hours, presentation mode -- whatever Windows is
/// currently calling the state in which it will not interrupt you.</param>
/// <param name="RestartPending">An update has been staged and is waiting for a reboot.</param>
/// <param name="FullDrive">The fixed drive closest to full, once it passes the point worth
/// mentioning. Null while every drive has room.</param>
public readonly record struct SystemConditions(
    bool DoNotDisturb,
    bool RestartPending,
    DriveSpace? FullDrive);

/// <param name="Name">The drive letter, with no trailing separator: <c>C:</c>.</param>
public readonly record struct DriveSpace(string Name, int PercentFree, long FreeBytes);
