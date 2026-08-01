using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Core.Models;
using Dock.Core.Services;

namespace Dock.Core.ViewModels;

/// <summary>
/// Quick notes jotted from the island. Only the most recent <see cref="MaxNotes"/> are ever kept --
/// this is a scratchpad for the last few things, not a notes app.
/// </summary>
public partial class NotesViewModel : ObservableObject
{
    private const int MaxNotes = 5;

    private readonly NotesStore _store;

    [ObservableProperty] private string _draftText = string.Empty;

    /// <summary>Newest first, so the panel never has to resort what it is already showing.</summary>
    public ObservableCollection<NoteEntry> Notes { get; }

    public NotesViewModel(NotesStore store)
    {
        _store = store;
        Notes = new ObservableCollection<NoteEntry>(_store.Load().Take(MaxNotes));
    }

    [RelayCommand]
    private void AddNote()
    {
        var text = DraftText.Trim();
        if (text.Length == 0)
            return;

        Notes.Insert(0, new NoteEntry(text, DateTimeOffset.Now));
        while (Notes.Count > MaxNotes)
            Notes.RemoveAt(Notes.Count - 1);

        DraftText = string.Empty;
        _store.Save(Notes.ToList());
    }
}
