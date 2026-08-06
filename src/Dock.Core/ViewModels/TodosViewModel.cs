using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Core.Models;
using Dock.Core.Services;

namespace Dock.Core.ViewModels;

/// <summary>
/// The island's todo list. A companion to <see cref="NotesViewModel"/>, and deliberately a
/// different thing: a note is a line you jotted and will read later, a todo is a line you intend
/// to tick off. Kept longer than notes for that reason -- a scratchpad can roll over, a task list
/// that quietly dropped its oldest item would lose work.
/// </summary>
public partial class TodosViewModel : ObservableObject
{
    private const int MaxTodos = 50;

    private readonly TodosStore _store;

    [ObservableProperty] private string _draftText = string.Empty;

    /// <summary>Open items first, then the done ones, newest first within each.</summary>
    public ObservableCollection<TodoItemViewModel> Todos { get; } = [];

    public TodosViewModel(TodosStore store)
    {
        _store = store;

        foreach (var entry in _store.Load().Take(MaxTodos))
            Todos.Add(new TodoItemViewModel(entry, Save));
    }

    /// <summary>How many are still open -- the number worth showing on a collapsed island.</summary>
    public int OpenCount => Todos.Count(t => !t.IsDone);

    [RelayCommand]
    private void AddTodo()
    {
        var text = DraftText.Trim();
        if (text.Length == 0)
            return;

        Todos.Insert(0, new TodoItemViewModel(new TodoEntry(text, DateTimeOffset.Now), Save));

        while (Todos.Count > MaxTodos)
            Todos.RemoveAt(Todos.Count - 1);

        DraftText = string.Empty;
        Save();
    }

    [RelayCommand]
    private void RemoveTodo(TodoItemViewModel? item)
    {
        if (item is null)
            return;

        Todos.Remove(item);
        Save();
    }

    /// <summary>Clears the ticked-off items, which is the only tidying this list needs.</summary>
    [RelayCommand]
    private void ClearDone()
    {
        foreach (var done in Todos.Where(t => t.IsDone).ToList())
            Todos.Remove(done);

        Save();
    }

    private void Save()
    {
        OnPropertyChanged(nameof(OpenCount));
        _store.Save(Todos.Select(t => t.Entry).ToList());
    }
}
