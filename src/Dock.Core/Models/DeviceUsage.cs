namespace Dock.Core.Models;

/// <summary>
/// One application holding the camera, right now.
///
/// A snapshot rather than a live object, for the same reason <see cref="MediaSnapshot"/> is: these
/// are read on a background thread and consumed on the dispatcher.
///
/// The microphone is deliberately not here. Windows records it in the same place and in the same
/// shape, but what it records is which applications have the device *open*, and on a real machine
/// that is an audio routing service holding it from boot and a chat client holding it for as long
/// as it runs. A light that is always on says nothing. The camera is the one of the two that turns
/// off again.
/// </summary>
/// <param name="AppPath">Full path to the executable, or empty for a packaged app -- those are
/// identified by package family name and have no path to point an icon extractor at.</param>
public sealed record DeviceUsage(string AppPath, string DisplayName);
