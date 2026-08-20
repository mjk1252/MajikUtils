namespace Dock.Core.Services;

/// <summary>
/// Puts something back on the real clipboard. One method per clipboard kind rather than one taking
/// a <c>ClipboardEntry</c>, so that Dock.Core keeps owning the model and the implementation -- which
/// is all Win32 and WPF imaging -- keeps owning nothing but the formats.
/// </summary>
public interface IClipboardWriter
{
    void SetText(string text);

    /// <summary>Puts a bitmap back, given the PNG bytes the history stored.</summary>
    void SetImage(byte[] png);

    /// <summary>
    /// Puts a file drop list back, so Ctrl+V in Explorer pastes real files rather than their names.
    /// </summary>
    void SetFiles(IReadOnlyList<string> paths);
}
