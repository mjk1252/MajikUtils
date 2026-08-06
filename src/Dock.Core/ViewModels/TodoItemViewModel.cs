using CommunityToolkit.Mvvm.ComponentModel;
using Dock.Core.Models;

namespace Dock.Core.ViewModels;

/// <summary>
/// One row of the todo list. Ticking the box writes straight through to the entry and tells the
/// list to persist -- there is no separate save gesture, and a todo that forgot it was done by the
/// next session would be worse than no list at all.
/// </summary>
public partial class TodoItemViewModel : ObservableObject
{
    private readonly Action _changed;

    public TodoEntry Entry { get; }

    public string Text => Entry.Text;

    public TodoItemViewModel(TodoEntry entry, Action changed)
    {
        Entry = entry;
        _changed = changed;
    }

    public bool IsDone
    {
        get => Entry.Done;
        set
        {
            if (Entry.Done == value)
                return;

            Entry.Done = value;
            OnPropertyChanged();
            _changed();
        }
    }
}
