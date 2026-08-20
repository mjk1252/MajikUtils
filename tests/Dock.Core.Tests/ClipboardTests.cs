using Dock.Core.Models;
using Dock.Core.Services;
using Dock.Core.ViewModels;

namespace Dock.Core.Tests;

public class ClipboardTests
{
    /// <summary>Records what was put back, and in which form.</summary>
    private sealed class FakeWriter : IClipboardWriter
    {
        public string? Text { get; private set; }
        public byte[]? Image { get; private set; }
        public IReadOnlyList<string>? Files { get; private set; }

        public void SetText(string text) => Text = text;
        public void SetImage(byte[] png) => Image = png;
        public void SetFiles(IReadOnlyList<string> paths) => Files = paths;
    }

    private static ClipboardEntry Image(byte[] bytes) =>
        ClipboardEntry.ForImage(bytes, 100, 50, DateTime.Now);

    // ---------------------------------------------------------------- identity

    [Fact]
    public void Signature_SameContent_Matches()
    {
        var a = ClipboardEntry.ForText("hello", DateTime.Now);
        var b = ClipboardEntry.ForText("hello", DateTime.Now.AddMinutes(1));

        Assert.Equal(a.Signature, b.Signature);
    }

    /// <summary>
    /// The kinds must not collide: a file literally named "hello" and the text "hello" are two
    /// different things to have copied, and only the signature keeps them apart in the history.
    /// </summary>
    [Fact]
    public void Signature_SameStringDifferentKind_Differs()
    {
        var text = ClipboardEntry.ForText("hello", DateTime.Now);
        var files = ClipboardEntry.ForFiles(["hello"], DateTime.Now);

        Assert.NotEqual(text.Signature, files.Signature);
    }

    [Fact]
    public void Signature_DifferentImages_Differ()
    {
        Assert.NotEqual(Image([1, 2, 3]).Signature, Image([3, 2, 1]).Signature);
    }

    [Fact]
    public void Signature_IdenticalImages_Match()
    {
        Assert.Equal(Image([1, 2, 3]).Signature, Image([1, 2, 3]).Signature);
    }

    // ---------------------------------------------------------------- descriptions

    [Fact]
    public void Text_ForAnImage_DescribesIt()
    {
        Assert.Equal("Image 1920 x 1080", ClipboardEntry.ForImage([1], 1920, 1080, DateTime.Now).Text);
    }

    [Fact]
    public void Text_ForOneFile_IsItsName()
    {
        Assert.Equal("report.pdf", ClipboardEntry.ForFiles([@"C:\docs\report.pdf"], DateTime.Now).Text);
    }

    [Fact]
    public void Text_ForSeveralFiles_Counts()
    {
        Assert.Equal("3 files", ClipboardEntry.ForFiles([@"a\1", @"a\2", @"a\3"], DateTime.Now).Text);
    }

    [Fact]
    public void ByteCost_CountsImagesOnly()
    {
        Assert.Equal(0, ClipboardEntry.ForText(new string('x', 5000), DateTime.Now).ByteCost);
        Assert.Equal(0, ClipboardEntry.ForFiles([@"C:\a", @"C:\b"], DateTime.Now).ByteCost);
        Assert.Equal(3, Image([1, 2, 3]).ByteCost);
    }

    // ---------------------------------------------------------------- putting it back

    [Fact]
    public void Copy_AnImage_GoesBackAsAnImage()
    {
        var writer = new FakeWriter();
        new ClipboardEntryViewModel(Image([9, 9]), writer).CopyCommand.Execute(null);

        Assert.Equal([9, 9], writer.Image);
        Assert.Null(writer.Text);
    }

    [Fact]
    public void Copy_Files_GoBackAsADropList()
    {
        var writer = new FakeWriter();
        var entry = ClipboardEntry.ForFiles([@"C:\a.txt", @"C:\b.txt"], DateTime.Now);

        new ClipboardEntryViewModel(entry, writer).CopyCommand.Execute(null);

        Assert.Equal([@"C:\a.txt", @"C:\b.txt"], writer.Files);
        Assert.Null(writer.Text);
    }

    [Fact]
    public void Copy_Text_GoesBackAsText()
    {
        var writer = new FakeWriter();
        new ClipboardEntryViewModel(ClipboardEntry.ForText("hello", DateTime.Now), writer)
            .CopyCommand.Execute(null);

        Assert.Equal("hello", writer.Text);
        Assert.Null(writer.Image);
    }

    // ---------------------------------------------------------------- the row

    [Fact]
    public void Files_AreNamedUpToThree_AndTheRestCounted()
    {
        var entry = ClipboardEntry.ForFiles(
            [@"C:\1.txt", @"C:\2.txt", @"C:\3.txt", @"C:\4.txt", @"C:\5.txt"], DateTime.Now);

        var vm = new ClipboardEntryViewModel(entry, new FakeWriter());

        Assert.Equal(["1.txt", "2.txt", "3.txt"], vm.Files.Select(f => f.Name));
        Assert.Equal(2, vm.ExtraFileCount);
    }

    [Fact]
    public void Preview_FlattensMultilineText()
    {
        var vm = new ClipboardEntryViewModel(
            ClipboardEntry.ForText("first\r\nsecond\tthird", DateTime.Now), new FakeWriter());

        Assert.Equal("first second third", vm.Preview);
    }

    // ---------------------------------------------------------------- the budget

    [Fact]
    public void Budget_UnderBudget_DropsNothing()
    {
        Assert.Equal(0, ClipboardBudget.Excess([10, 10, 10], 100));
    }

    [Fact]
    public void Budget_OverBudget_DropsTheOldestFirst()
    {
        // 40 over a budget of 25: dropping the last two leaves 20.
        Assert.Equal(2, ClipboardBudget.Excess([10, 10, 10, 10], 25));
    }

    /// <summary>
    /// The rule that makes this worth a function of its own. A copy larger than the entire budget
    /// still has to land -- whatever was on the clipboard before it is already gone.
    /// </summary>
    [Fact]
    public void Budget_ANewEntryBiggerThanTheBudget_IsStillKept()
    {
        Assert.Equal(2, ClipboardBudget.Excess([500, 10, 10], 100));
    }

    [Fact]
    public void Budget_TextOnlyHistory_IsNeverTrimmed()
    {
        Assert.Equal(0, ClipboardBudget.Excess([0, 0, 0, 0, 0], 0));
    }
}
