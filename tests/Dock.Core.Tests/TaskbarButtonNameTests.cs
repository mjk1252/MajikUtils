using Dock.Core.Models;

namespace Dock.Core.Tests;

/// <summary>
/// The strings here are the real ones, read off a running Windows 11 taskbar. That matters more
/// than usual: this parser exists because the shell has no API for badges, so what it announces to
/// an accessibility client is the whole contract, and a test written against an invented string
/// would be testing nothing.
///
/// The <see cref="RealDiscordButton"/> cases earned their own names. The first version of this
/// looked for the badge in the button's accessible name, shipped, and read nothing at all -- the
/// name is identical whether or not the app has unread messages, and the badge is in the help text.
/// </summary>
public class TaskbarButtonNameTests
{
    private const string Notepad = "Appid: Microsoft.WindowsNotepad_8wekyb3d8bbwe!App";

    [Fact]
    public void ReadBadge_ReadsTheCountAndTheAppName()
    {
        var badge = TaskbarButtonName.ReadBadge(
            "Microsoft Outlook - 3 new notifications - 1 running window pinned", Notepad, null);

        Assert.NotNull(badge);
        Assert.Equal("Microsoft Outlook", badge.Value.AppName);
        Assert.Equal(3, badge.Value.Count);
        Assert.Equal("Microsoft.WindowsNotepad_8wekyb3d8bbwe!App", badge.Value.AppUserModelId);
    }

    [Theory]
    [InlineData("File Pilot - 1 running window pinned")]
    [InlineData("Opera browser - 2 running windows pinned")]
    [InlineData("Brave pinned")]
    [InlineData("Steam - 1 running window")]
    public void ReadBadge_IgnoresAButtonThatIsMerelyRunning(string name) =>
        Assert.Null(TaskbarButtonName.ReadBadge(name, Notepad, null));

    /// <summary>
    /// A badge can be a dot with no number on it, and that is not the same as no badge -- something
    /// is still waiting. Zero is the answer, null is not.
    /// </summary>
    [Fact]
    public void ReadBadge_TreatsANumberlessBadgeAsABadge()
    {
        var badge = TaskbarButtonName.ReadBadge("Discord - new notification - 1 running window", Notepad, null);

        Assert.NotNull(badge);
        Assert.Equal(0, badge.Value.Count);
        Assert.Equal("Discord", badge.Value.AppName);
    }

    [Fact]
    public void ReadBadge_ReadsAnOverflowCountByItsLeadingDigits()
    {
        var badge = TaskbarButtonName.ReadBadge("Mail - 99+ new notifications pinned", Notepad, null);

        Assert.Equal(99, badge!.Value.Count);
    }

    /// <summary>
    /// Everything in the tray is a Button too: Start, Widgets, the clock. None of them carry an
    /// AppUserModelID, and that is what tells an app button from the rest.
    /// </summary>
    [Theory]
    [InlineData("Notifications 5 new notifications (Do not disturb on)", "SystemTrayIcon")]
    [InlineData("Widgets 23 degrees Sunny", "WidgetsButton")]
    [InlineData("Start", "StartButton")]
    public void ReadBadge_RejectsAnythingThatIsNotAnAppButton(string name, string automationId) =>
        Assert.Null(TaskbarButtonName.ReadBadge(name, automationId, null));

    [Fact]
    public void ReadBadge_RejectsEmptyInput()
    {
        Assert.Null(TaskbarButtonName.ReadBadge(null, Notepad, null));
        Assert.Null(TaskbarButtonName.ReadBadge("", Notepad, null));
        Assert.Null(TaskbarButtonName.ReadBadge("Outlook - 3 new notifications", null, null));
        Assert.Null(TaskbarButtonName.ReadBadge("Outlook - 3 new notifications", "Appid: ", null));
    }

    /// <summary>
    /// Discord, as actually observed. The name is the same string in all three states; everything
    /// that distinguishes them is in the help text, which is what makes it the property to read.
    /// </summary>
    [Theory]
    [InlineData("", null)]
    [InlineData("0 notifications", null)]
    [InlineData("Unread messages", 0)]
    [InlineData("Attention requested, 0 notifications", 0)]
    [InlineData("Attention requested", 0)]
    [InlineData("3 notifications", 3)]
    public void ReadBadge_RealDiscordButton(string helpText, int? expected)
    {
        const string name = "Discord - 1 running window pinned";
        const string appId = "Appid: com.squirrel.Discord.Discord";

        var badge = TaskbarButtonName.ReadBadge(name, appId, helpText);

        Assert.Equal(expected, badge?.Count);

        if (expected is not null)
            Assert.Equal("Discord", badge!.Value.AppName);
    }

    /// <summary>
    /// Two independent facts joined by a comma, and reading them as one throws the badge away.
    /// "Attention requested" is the dot; "0 notifications" is the numeric channel saying it has no
    /// number to give. Taking the count as authoritative concludes there is no badge, while the
    /// button is sitting there with a dot on it -- which is exactly the bug this caught.
    /// </summary>
    [Fact]
    public void ReadBadge_ReadsADotEvenWhenTheCountChannelSaysZero()
    {
        var badge = TaskbarButtonName.ReadBadge(
            "Discord - 1 running window pinned",
            "Appid: com.squirrel.Discord.Discord",
            "Attention requested, 0 notifications");

        Assert.NotNull(badge);
        Assert.Equal(0, badge.Value.Count);
    }

    /// <summary>A real count still wins over the wording when there is one.</summary>
    [Fact]
    public void ReadBadge_PrefersARealCountOverADot()
    {
        var badge = TaskbarButtonName.ReadBadge(
            "Mail pinned", Notepad, "Attention requested, 4 notifications");

        Assert.Equal(4, badge!.Value.Count);
    }

    /// <summary>
    /// The distinction the whole parser turns on. "0 notifications" is a button saying it has
    /// nothing; a wordless badge is a button with a dot on it. One is null, the other is zero, and
    /// treating them alike either loses Discord's badge or invents one for every app on the bar.
    /// </summary>
    [Fact]
    public void ReadBadge_TellsAnEmptyBadgeFromAWordlessOne()
    {
        Assert.Null(TaskbarButtonName.ReadBadge("Discord pinned", Notepad, "0 notifications"));
        Assert.Equal(0, TaskbarButtonName.ReadBadge("Discord pinned", Notepad, "Unread messages")?.Count);
    }

    /// <summary>
    /// Help text that has nothing to do with badges must not become one. Plenty of buttons carry a
    /// tooltip, and a parser that read any non-empty string as a dot would light the island up for
    /// every app on the taskbar.
    /// </summary>
    [Theory]
    [InlineData("Open a new window")]
    [InlineData("Pin to taskbar")]
    [InlineData("Running")]
    public void ReadBadge_IgnoresHelpTextThatIsNotAboutNotifications(string helpText) =>
        Assert.Null(TaskbarButtonName.ReadBadge("Steam - 1 running window", Notepad, helpText));

    /// <summary>Help text is where the badge is, so it wins over anything the name says.</summary>
    [Fact]
    public void ReadBadge_PrefersHelpTextOverTheName()
    {
        var badge = TaskbarButtonName.ReadBadge(
            "Mail - 2 new notifications pinned", Notepad, "7 notifications");

        Assert.Equal(7, badge!.Value.Count);
    }

    [Fact]
    public void ReadNotificationCentreCount_ReadsTheTrayTotal()
    {
        Assert.Equal(5, TaskbarButtonName.ReadNotificationCentreCount(
            "Notifications 5 new notifications (Do not disturb on)"));

        Assert.Equal(0, TaskbarButtonName.ReadNotificationCentreCount("Notifications No new notifications"));
        Assert.Equal(0, TaskbarButtonName.ReadNotificationCentreCount("Clock 16:58"));
        Assert.Equal(0, TaskbarButtonName.ReadNotificationCentreCount(null));
    }

    /// <summary>
    /// The one behaviour worth stating outright, because the whole class rests on a string nobody
    /// promised to keep: an unrecognised name is a button with no badge, never an exception.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("- - -")]
    [InlineData("3 new notifications")]
    public void ReadBadge_NeverThrowsOnSomethingItDoesNotUnderstand(string name)
    {
        var exception = Record.Exception(() => TaskbarButtonName.ReadBadge(name, Notepad, null));

        Assert.Null(exception);
    }
}
