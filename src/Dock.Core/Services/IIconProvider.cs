namespace Dock.Core.Services;

public interface IIconProvider
{
    byte[]? GetIconPng(string path, int size);

    /// <summary>
    /// The icon for something the shell knows only by AppUserModelID, which is how a taskbar
    /// button identifies itself.
    ///
    /// A separate method rather than a smarter <see cref="GetIconPng"/>, because the two are asking
    /// genuinely different questions of the shell: one is "what does this file look like", and the
    /// other is "what does the thing registered under this id look like". Plenty of AppUserModelIDs
    /// -- every packaged app, for one -- have no path at all to ask the first question about.
    ///
    /// Null when the id resolves to nothing, which is ordinary: an app can be uninstalled between
    /// the taskbar publishing its button and anybody asking about it.
    /// </summary>
    byte[]? GetAppIconPng(string appUserModelId, int size);
}
