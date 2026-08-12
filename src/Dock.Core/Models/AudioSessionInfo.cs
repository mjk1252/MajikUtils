namespace Dock.Core.Models;

/// <summary>
/// One application's audio session, as read off Core Audio. Immutable and read-only by design --
/// <c>VolumeMixerActivity</c> is what turns a set of these into something that can be dragged, the
/// same split <see cref="MediaSnapshot"/> and <c>MediaViewModel</c> already draw between a reading
/// and its view model.
/// </summary>
public sealed record AudioSessionInfo(
    int ProcessId,
    string ExecutablePath,
    string DisplayName,
    double Volume,
    bool IsMuted,
    bool IsActive);
