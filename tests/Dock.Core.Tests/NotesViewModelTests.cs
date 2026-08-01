using Dock.Core.Services;
using Dock.Core.ViewModels;

namespace Dock.Core.Tests;

public class NotesViewModelTests
{
    [Fact]
    public void AddNote_AppendsToFront()
    {
        var viewModel = new NotesViewModel(new NotesStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())));

        viewModel.DraftText = "first";
        viewModel.AddNoteCommand.Execute(null);
        viewModel.DraftText = "second";
        viewModel.AddNoteCommand.Execute(null);

        Assert.Equal(["second", "first"], viewModel.Notes.Select(n => n.Text));
        Assert.Equal(string.Empty, viewModel.DraftText);
    }

    [Fact]
    public void AddNote_Blank_IsIgnored()
    {
        var viewModel = new NotesViewModel(new NotesStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())));

        viewModel.DraftText = "   ";
        viewModel.AddNoteCommand.Execute(null);

        Assert.Empty(viewModel.Notes);
    }

    [Fact]
    public void AddNote_PastFive_DropsTheOldest()
    {
        var viewModel = new NotesViewModel(new NotesStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())));

        for (var i = 1; i <= 6; i++)
        {
            viewModel.DraftText = $"note {i}";
            viewModel.AddNoteCommand.Execute(null);
        }

        Assert.Equal(5, viewModel.Notes.Count);
        Assert.Equal("note 6", viewModel.Notes[0].Text);
        Assert.DoesNotContain(viewModel.Notes, n => n.Text == "note 1");
    }

    [Fact]
    public void Notes_PersistAcrossInstances()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        var first = new NotesViewModel(new NotesStore(path));
        first.DraftText = "remember this";
        first.AddNoteCommand.Execute(null);

        var second = new NotesViewModel(new NotesStore(path));

        Assert.Equal("remember this", second.Notes.Single().Text);
    }
}
