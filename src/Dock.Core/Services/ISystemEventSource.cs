namespace Dock.Core.Services;

/// <summary>
/// The momentary things worth announcing on the island: a download finishing, a screenshot landing,
/// a drive appearing, the network coming or going.
///
/// One interface for all of them because the island treats them identically -- a line of text, a
/// glyph, two and a half seconds. What differs is only where each is noticed, which is the
/// implementation's business and nothing the island needs to know.
/// </summary>
public interface ISystemEventSource
{
    event EventHandler<SystemEvent>? Occurred;

    void Start();
    void Stop();
}

/// <param name="Glyph">A Segoe Fluent Icons codepoint. The compact form is this alone.</param>
/// <param name="Detail">Optional particulars -- a filename, a drive letter. Often blank.</param>
public readonly record struct SystemEvent(string Label, string Glyph, string Detail = "");
