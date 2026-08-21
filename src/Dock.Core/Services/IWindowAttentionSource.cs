namespace Dock.Core.Services;

/// <summary>
/// Which applications are asking for your attention right now.
///
/// The third source, and the one that finally catches what the other two cannot. A chat
/// application that draws its own notifications raises no Windows toast, so the notification
/// centre never hears about it; and its taskbar badge is unreadable while the taskbar is hidden,
/// which is the case this whole feature exists for. What it *does* do is flash its taskbar button,
/// and that is a window-manager event rather than anything drawn -- which means it arrives whether
/// or not the taskbar is on screen, and needs no string parsing to understand.
///
/// It answers a narrower question than the other two: *that* an application wants you, never how
/// many things are waiting. Windows does not know the number either -- a flash is a flash.
/// </summary>
public interface IWindowAttentionSource
{
    /// <summary>Raised only when the set of applications asking for attention changes.</summary>
    event EventHandler<IReadOnlyList<AttentionRequest>>? Changed;

    void Start();
    void Stop();
}

/// <param name="AppUserModelId">How the app is identified for an icon. The executable path here,
/// since a flashing window is a process rather than a registered app id -- which
/// <c>GetAppIconPng</c> handles, as it tries a real path before the Applications folder.</param>
/// <param name="AppName">Its display name, from the executable's own version information where it
/// has any, and the file name where it does not.</param>
public readonly record struct AttentionRequest(string AppUserModelId, string AppName);
