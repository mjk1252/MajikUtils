using CommunityToolkit.Mvvm.ComponentModel;

namespace Dock.Core.ViewModels;

/// <summary>
/// A momentary announcement: something happened, here it is for a couple of seconds, then it goes.
///
/// One instance shared by every source that has this shape -- a copy, a screenshot, a download
/// finishing, a drive appearing, the network changing. They could each have been an activity of
/// their own, but six activities all at the same rank would spend their time queueing behind each
/// other for two slots, and the honest behaviour of an on-screen display is that the newest thing
/// replaces whatever was there. So they share one, and the last to speak wins.
///
/// Announcements do not linger. The whole activity *is* the grace period -- it is already showing
/// something that has finished happening -- so when it expires it should go at once rather than
/// hang about for another second and a half.
/// </summary>
public sealed partial class AnnouncementActivity : ObservableObject, IIslandActivity
{
    /// <summary>
    /// How long an announcement stays up. Long enough to read four or five words without looking
    /// away from what you were doing, short enough not to sit on the music.
    /// </summary>
    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(2.5);

    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    [ObservableProperty] private bool _isActive;

    /// <summary>The headline, e.g. "Screenshot captured".</summary>
    [ObservableProperty] private string _label = string.Empty;

    /// <summary>The particulars, e.g. a filename. Blank is fine and common.</summary>
    [ObservableProperty] private string _detail = string.Empty;

    /// <summary>A Segoe Fluent Icons glyph. The compact form is this and nothing else.</summary>
    [ObservableProperty] private string _glyph = string.Empty;

    public string Key => "announcement";

    /// <summary>
    /// Above music and above any standing condition: something that just happened and will be gone
    /// in two seconds has a much better claim on the pill than either.
    /// </summary>
    public IslandPriority Priority => IslandPriority.Transient;

    public TimeSpan Linger => TimeSpan.Zero;

    /// <summary>
    /// Says something, replacing whatever was being said. Also restarts the clock -- turning the
    /// volume knob twice should read as one announcement that lasted longer, not two that
    /// overlapped.
    /// </summary>
    public void Announce(DateTimeOffset now, string label, string glyph, string detail = "")
    {
        Label = label;
        Glyph = glyph;
        Detail = detail;

        _expiresAt = now + Duration;
        IsActive = true;
    }

    /// <summary>
    /// Retires the announcement once its moment has passed. Driven by the App's activity clock, so
    /// this class holds no timer and a test can walk time forward without waiting.
    /// </summary>
    public void Tick(DateTimeOffset now)
    {
        if (IsActive && now >= _expiresAt)
            IsActive = false;
    }

    public void Retire()
    {
        Label = string.Empty;
        Detail = string.Empty;
        Glyph = string.Empty;
    }
}
