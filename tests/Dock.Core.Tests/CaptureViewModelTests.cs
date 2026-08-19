using Dock.Core.Services;
using Dock.Core.ViewModels;

namespace Dock.Core.Tests;

public class CaptureViewModelTests
{
    private static CaptureViewModel Build(out TodosViewModel todos, out NotesViewModel notes)
    {
        todos = new TodosViewModel(new TodosStore(TempPath()));
        notes = new NotesViewModel(new NotesStore(TempPath()));
        return new CaptureViewModel(todos, notes);
    }

    private static string TempPath() => Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    [Theory]
    [InlineData("25m", 25)]
    [InlineData("25 min", 25)]
    [InlineData("90m", 90)]
    [InlineData("1h", 60)]
    [InlineData("1h30", 90)]
    [InlineData("1h 30m", 90)]
    public void Parse_ADuration_IsATimer(string draft, int minutes)
    {
        var intent = CaptureViewModel.Parse(draft);

        Assert.Equal(CaptureKind.Timer, intent.Kind);
        Assert.Equal(TimeSpan.FromMinutes(minutes), intent.Duration);
    }

    /// <summary>
    /// The grammar earns its keep only if it stays out of the way of ordinary typing, and every
    /// one of these is a task somebody would plausibly write.
    /// </summary>
    [Theory]
    [InlineData("book the 25m demo")]
    [InlineData("25 minutes with Sam")]
    [InlineData("m")]
    [InlineData("email h")]
    public void Parse_TextThatMerelyMentionsALength_IsStillATask(string draft)
    {
        Assert.Equal(CaptureKind.Todo, CaptureViewModel.Parse(draft).Kind);
    }

    [Fact]
    public void Parse_LeadingSlash_IsASearch()
    {
        var intent = CaptureViewModel.Parse("/firefox");

        Assert.Equal(CaptureKind.Search, intent.Kind);
        Assert.Equal("firefox", intent.Text);
    }

    [Fact]
    public void Parse_LeadingDot_IsANote()
    {
        var intent = CaptureViewModel.Parse(".wifi password is on the router");

        Assert.Equal(CaptureKind.Note, intent.Kind);
        Assert.Equal("wifi password is on the router", intent.Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".")]
    [InlineData(null)]
    public void Parse_NothingUsable_IsNone(string? draft)
    {
        Assert.Equal(CaptureKind.None, CaptureViewModel.Parse(draft).Kind);
    }

    [Fact]
    public void Submit_ATask_LandsInTheFeedAndClearsTheBox()
    {
        var capture = Build(out var todos, out _);

        capture.DraftText = "renew the domain";
        var intent = capture.Submit(DateTimeOffset.UtcNow, new TimerActivity());

        Assert.Equal(CaptureKind.Todo, intent.Kind);
        Assert.Equal("renew the domain", Assert.Single(todos.Todos).Text);
        Assert.Equal("renew the domain", Assert.Single(capture.Items).Text);
        Assert.Equal(string.Empty, capture.DraftText);
    }

    [Fact]
    public void Submit_ADuration_StartsTheTimerAndFilesNothing()
    {
        var capture = Build(out var todos, out var notes);
        var timer = new TimerActivity();

        capture.DraftText = "25m";
        capture.Submit(DateTimeOffset.UtcNow, timer);

        Assert.True(timer.IsActive);
        Assert.Empty(todos.Todos);
        Assert.Empty(notes.Notes);
    }

    /// <summary>
    /// The launcher is a place in a window, so the view model reports the intent and does nothing
    /// with it. The box still empties: the query has moved on to the search field.
    /// </summary>
    [Fact]
    public void Submit_ASearch_ComesBackForTheCallerToRoute()
    {
        var capture = Build(out var todos, out var notes);

        capture.DraftText = "/vscode";
        var intent = capture.Submit(DateTimeOffset.UtcNow, new TimerActivity());

        Assert.Equal(CaptureKind.Search, intent.Kind);
        Assert.Equal("vscode", intent.Text);
        Assert.Empty(todos.Todos);
        Assert.Empty(notes.Notes);
        Assert.Equal(string.Empty, capture.DraftText);
    }

    [Fact]
    public void Feed_MergesBothSources_NewestFirst()
    {
        var capture = Build(out var todos, out var notes);

        notes.DraftText = "older note";
        notes.AddNoteCommand.Execute(null);
        todos.DraftText = "newer task";
        todos.AddTodoCommand.Execute(null);

        Assert.Equal(["newer task", "older note"], capture.Items.Select(i => i.Text));
        Assert.Equal([true, false], capture.Items.Select(i => i.IsTodo));
    }

    [Fact]
    public void Feed_PastTheCap_CountsTheRestRatherThanDroppingThem()
    {
        var capture = Build(out var todos, out _);

        for (var i = 0; i < CaptureViewModel.MaxItems + 3; i++)
        {
            todos.DraftText = $"task {i}";
            todos.AddTodoCommand.Execute(null);
        }

        Assert.Equal(CaptureViewModel.MaxItems, capture.Items.Count);
        Assert.Equal(3, capture.OverflowCount);
    }

    /// <summary>
    /// Ticking a box must not reorder the feed: the row has to stay under the pointer that ticked
    /// it. What it does have to do is bring "Clear done" out.
    /// </summary>
    [Fact]
    public void TickingATask_LeavesItInPlaceAndOffersToClear()
    {
        var capture = Build(out var todos, out _);

        todos.DraftText = "first";
        todos.AddTodoCommand.Execute(null);
        todos.DraftText = "second";
        todos.AddTodoCommand.Execute(null);

        Assert.False(capture.HasDone);

        capture.Items[1].Todo!.IsDone = true;

        Assert.True(capture.HasDone);
        Assert.Equal(["second", "first"], capture.Items.Select(i => i.Text));

        capture.ClearDoneCommand.Execute(null);

        Assert.Equal(["second"], capture.Items.Select(i => i.Text));
        Assert.False(capture.HasDone);
    }

    [Fact]
    public void Remove_TakesTheTaskOutOfTheFeed()
    {
        var capture = Build(out var todos, out _);

        todos.DraftText = "mistake";
        todos.AddTodoCommand.Execute(null);

        capture.RemoveCommand.Execute(capture.Items[0]);

        Assert.Empty(todos.Todos);
        Assert.Empty(capture.Items);
    }
}
