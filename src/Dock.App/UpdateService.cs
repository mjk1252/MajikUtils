using System.IO;
using System.Net.Http;
using Velopack;
using Velopack.Sources;

namespace Dock.App;

/// <summary>
/// Checks GitHub Releases for a newer build and downloads it in the background.
///
/// Never surfaces a failure: a machine offline, GitHub unreachable, no release ever published --
/// all read the same as "nothing to update", the same degrade-quietly rule every interop source in
/// this app already follows. The one thing worth telling anyone is the opposite case, once a
/// download actually finishes -- see <see cref="UpdateReady"/>.
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

    /// <summary>
    /// Looks for a newer release and downloads it if one exists. A no-op outside an installed
    /// copy -- a build run straight from bin/Debug has no Velopack install backing it to update,
    /// which is exactly the case <see cref="UpdateManager.IsInstalled"/> exists to catch.
    /// </summary>
    public async Task CheckAndDownloadAsync()
    {
        if (UpdateReady || !_manager.IsInstalled)
            return;

        try
        {
            var update = await _manager.CheckForUpdatesAsync();
            if (update is null)
                return;

            await _manager.DownloadUpdatesAsync(update);
            _pending = update;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            // No network, GitHub unreachable, or the download was interrupted. Worth trying again
            // on the next check, not worth interrupting anyone about now.
        }
    }

    /// <summary>Swaps in the downloaded update and restarts MajikUtils. Does not return.</summary>
    public void ApplyAndRestart()
    {
        if (_pending is { } update)
            _manager.ApplyUpdatesAndRestart(update);
    }
}
