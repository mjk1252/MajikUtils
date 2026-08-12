using System.IO;
using System.Net.Http;
using Velopack;
using Velopack.Sources;

namespace Dock.App;

/// <summary>
/// What came of a check. The background timer only ever cares about
/// <see cref="UpdateCheckResult.UpdateReady"/>; a check the user asked for by clicking a button
/// deserves an answer regardless of which of the four this is -- that split is why this is a
/// return value rather than the silent no-op <see cref="UpdateService"/> used to be.
/// </summary>
public enum UpdateCheckResult
{
    UpToDate,
    UpdateReady,

    /// <summary>A dev build run straight from bin/Debug, with no Velopack install behind it.</summary>
    NotInstalled,

    /// <summary>No network, GitHub unreachable, or the download was interrupted.</summary>
    CheckFailed
}

/// <summary>
/// Checks GitHub Releases for a newer build and downloads it in the background.
///
/// The background timer in <c>App</c> only acts on the good outcome, same as before; a check run
/// from the gear menu's "Check for updates" surfaces whichever <see cref="UpdateCheckResult"/> it
/// gets, because someone who just clicked something is owed an answer even when that answer is
/// "no, and I don't know why."
/// </summary>
public sealed class UpdateService
{
    // The repo, not a file server: GithubSource reads owner/repo out of this URL and resolves
    // release assets from GitHub's own API, so "hosting updates" costs nothing beyond attaching
    // files to a release.
    private const string RepoUrl = "https://github.com/mjk1252/MajikUtils";

    private readonly UpdateManager _manager = new(new GithubSource(RepoUrl, null, false));
    private UpdateInfo? _pending;

    /// <summary>Whether a downloaded update is sitting there waiting for a restart.</summary>
    public bool UpdateReady => _pending is not null;

    /// <summary>Looks for a newer release and downloads it if one exists.</summary>
    public async Task<UpdateCheckResult> CheckAndDownloadAsync()
    {
        if (UpdateReady)
            return UpdateCheckResult.UpdateReady;

        if (!_manager.IsInstalled)
            return UpdateCheckResult.NotInstalled;

        try
        {
            var update = await _manager.CheckForUpdatesAsync();
            if (update is null)
                return UpdateCheckResult.UpToDate;

            await _manager.DownloadUpdatesAsync(update);
            _pending = update;
            return UpdateCheckResult.UpdateReady;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            return UpdateCheckResult.CheckFailed;
        }
    }

    /// <summary>Swaps in the downloaded update and restarts MajikUtils. Does not return.</summary>
    public void ApplyAndRestart()
    {
        if (_pending is { } update)
            _manager.ApplyUpdatesAndRestart(update);
    }
}
