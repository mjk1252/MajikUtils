using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Core.Models;

namespace Dock.Core.ViewModels;

/// <summary>What a line typed into the island's capture box turned out to mean.</summary>
public enum CaptureKind
{
    /// <summary>Nothing usable was typed. The caller does nothing at all.</summary>
    None,

    /// <summary>A duration: start a countdown.</summary>
    Timer,

    /// <summary>A query for the launcher, the one intent this view model cannot act on itself.</summary>
    Search,

    Note,
    Todo
}

/// <summary>
/// The reading of one captured line: what it meant, and the payload for whichever form that takes
/// -- text for a search or an entry, a duration for a timer.
/// </summary>
public readonly record struct CaptureIntent(CaptureKind Kind, string Text, TimeSpan Duration)
{
    public static readonly CaptureIntent None = new(CaptureKind.None, string.Empty, TimeSpan.Zero);
}

/// <summary>
/// The island's one input, and the feed of what has been put through it.
///
/// This replaces three stacked modules -- a row of timer chips, a todo box and a notes box -- that
/// between them wanted three headers, three text fields and a standing decision about which of
/// them the caret belonged in. All three were the same gesture: type a line, press Enter, get on
/// with it. So there is one box, and what you typed decides what it becomes.
///
/// The grammar is deliberately tiny, because a syntax nobody remembers is worse than a second
/// button. A bare duration starts a timer, a leading slash searches, a leading dot files a note,
/// and everything else -- the common case, and so the one that needs no prefix at all -- is a task.
///
/// Parsing is a pure static, which is the point of it living here rather than in the window: the
/// whole grammar can be asserted without a UI.
/// </summary>
public partial class CaptureViewModel : ObservableObject
{
    /// <summary>
    /// How many entries the feed shows. The island hangs off the top edge and grows downwards over
    /// whatever is behind it, so this is a layout budget rather than a preference: the todo list
    /// holds fifty, and a panel fifty rows tall is a window. The rest are counted rather than
    /// dropped -- see <see cref="OverflowCount"/>.
    /// </summary>
    public const int MaxItems = 7;

    /// <summary>
    /// Bare durations only: "25m", "1h", "1h30", "1h 30m", "90m". Anchored at both ends, so a task
    /// that merely mentions a length ("book the 25m demo") stays a task.
    ///
    /// The two alternatives differ over whether the unit may be left off. After an hours group
    /// there is nothing else a trailing number could be, so "1h30" is allowed; on its own "25" is
    /// far likelier to be the start of a task than a countdown, so the minutes-only form insists
    /// on its "m".
    /// </summary>
    [GeneratedRegex(@"^(?:(?<h>\d+)\s*h(?:\s*(?<m>\d+)\s*(?:min|m)?)?|(?<mo>\d+)\s*(?:min|m))$",
        RegexOptions.IgnoreCase)]
    private static partial Regex DurationPattern();

    private readonly TodosViewModel _todos;
    private readonly NotesViewModel _notes;

    [ObservableProperty] private string _draftText = string.Empty;

    /// <summary>
    /// Tasks and notes in one list, newest first. They were separated because a note is something
    /// you will read and a task is something you will tick -- a real distinction, and a bad reason
    /// to split the surface in two: both are captured in the same moment and re-read in the same
    /// moment. The difference survives as a checkbox on the row.
    /// </summary>
    public ObservableCollection<CaptureItemViewModel> Items { get; } = [];

    /// <summary>How many entries exist beyond the ones <see cref="Items"/> is showing.</summary>
    [ObservableProperty] private int _overflowCount;

    [ObservableProperty] private bool _hasItems;

    /// <summary>
    /// Whether anything in the feed has been ticked off, which is the only condition under which
    /// "Clear done" is worth a line of the panel.
    /// </summary>
    [ObservableProperty] private bool _hasDone;

    public CaptureViewModel(TodosViewModel todos, NotesViewModel notes)
    {
        _todos = todos;
        _notes = notes;

        _todos.Todos.CollectionChanged += OnSourceChanged;
        _notes.Notes.CollectionChanged += OnSourceChanged;

        Rebuild();
    }

    private void OnSourceChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    /// <summary>
    /// Reprojects both sources into the merged feed. Rebuilt wholesale rather than patched: the
    /// two lists total fifty-five entries at their absolute limit, and a merge-in-place would be
    /// more code than the saving is worth.
    ///
    /// Ticking a box deliberately does not land here -- the feed is chronological, so a finished
    /// task stays exactly where it was rather than jumping out from under the pointer that ticked it.
    /// </summary>
    private void Rebuild()
    {
        foreach (var todo in _todos.Todos)
        {
            // Idempotent: subscribing the same handler to the same task twice would double-count
            // nothing, but it would run the refresh twice per tick, and this rebuilds often.
            todo.PropertyChanged -= OnTodoChanged;
            todo.PropertyChanged += OnTodoChanged;
        }

        var merged = _todos.Todos.Select(CaptureItemViewModel.For)
            .Concat(_notes.Notes.Select(CaptureItemViewModel.For))
            .OrderByDescending(item => item.CreatedAt)
            .ToList();

        Items.Clear();
        foreach (var item in merged.Take(MaxItems))
            Items.Add(item);

        OverflowCount = Math.Max(0, merged.Count - MaxItems);
        HasItems = Items.Count > 0;
        RefreshHasDone();
    }

    /// <summary>
    /// Ticking a box does not touch either collection, so nothing else would notice it. The row
    /// stays where it is on purpose -- see <see cref="Rebuild"/> -- and only the one line that
    /// depends on the tick is recomputed.
    /// </summary>
    private void OnTodoChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TodoItemViewModel.IsDone))
            RefreshHasDone();
    }

    private void RefreshHasDone() => HasDone = _todos.Todos.Any(todo => todo.IsDone);

    [RelayCommand]
    private void ClearDone() => _todos.ClearDoneCommand.Execute(null);

    [RelayCommand]
    private void Remove(CaptureItemViewModel? item)
    {
        if (item?.Todo is { } todo)
            _todos.RemoveTodoCommand.Execute(todo);
    }

    /// <summary>
    /// Reads the draft, acts on whatever it can act on itself, and clears the box. A
    /// <see cref="CaptureKind.Search"/> comes back untouched for the caller to route -- the
    /// launcher is a section of a window, and this class knows nothing about windows.
    ///
    /// <paramref name="now"/> is passed in for the same reason <see cref="TimerActivity"/> takes
    /// one: a test should be able to start a timer without waiting for it.
    /// </summary>
    public CaptureIntent Submit(DateTimeOffset now, TimerActivity timer)
    {
        var intent = Parse(DraftText);

        switch (intent.Kind)
        {
            case CaptureKind.None:
                return intent;

            case CaptureKind.Timer:
                timer.Start(now, intent.Duration);
                break;

            case CaptureKind.Note:
                _notes.DraftText = intent.Text;
                _notes.AddNoteCommand.Execute(null);
                break;

            case CaptureKind.Todo:
                _todos.DraftText = intent.Text;
                _todos.AddTodoCommand.Execute(null);
                break;

            case CaptureKind.Search:
                // Left to the caller. The box is still cleared, because the query has moved into
                // the launcher's own field and two copies of it on screen is one too many.
                break;
        }

        DraftText = string.Empty;
        return intent;
    }

    /// <summary>The whole grammar. Pure, static and total: every string is one of the five kinds.</summary>
    public static CaptureIntent Parse(string? draft)
    {
        var text = (draft ?? string.Empty).Trim();
        if (text.Length == 0)
            return CaptureIntent.None;

        if (text[0] == '/')
            return new CaptureIntent(CaptureKind.Search, text[1..].Trim(), TimeSpan.Zero);

        if (text[0] == '.')
        {
            var note = text[1..].Trim();
            return note.Length == 0
                ? CaptureIntent.None
                : new CaptureIntent(CaptureKind.Note, note, TimeSpan.Zero);
        }

        var duration = ParseDuration(text);
        if (duration > TimeSpan.Zero)
            return new CaptureIntent(CaptureKind.Timer, text, duration);

        return new CaptureIntent(CaptureKind.Todo, text, TimeSpan.Zero);
    }

    /// <summary>
    /// A duration, or zero for anything that is not one. "1h30" is accepted as well as "1h30m":
    /// the trailing unit is the one people leave off, and after an hours group what remains can
    /// only be minutes either way.
    /// </summary>
    private static TimeSpan ParseDuration(string text)
    {
        var match = DurationPattern().Match(text);
        if (!match.Success)
            return TimeSpan.Zero;

        var hours = match.Groups["h"];
        var minutes = match.Groups["m"];
        var minutesOnly = match.Groups["mo"];

        var total = TimeSpan.Zero;

        if (hours.Success)
            total += TimeSpan.FromHours(int.Parse(hours.Value));

        if (minutes.Success)
            total += TimeSpan.FromMinutes(int.Parse(minutes.Value));

        if (minutesOnly.Success)
            total += TimeSpan.FromMinutes(int.Parse(minutesOnly.Value));

        return total;
    }
}

/// <summary>
/// One row of the merged feed: a task or a note, flattened onto the handful of values the row
/// draws. A task keeps its own view model behind <see cref="Todo"/>, so the checkbox still writes
/// straight through to storage.
/// </summary>
public sealed class CaptureItemViewModel
{
    private CaptureItemViewModel(TodoItemViewModel? todo, NoteEntry? note)
    {
        Todo = todo;
        Note = note;
    }

    public static CaptureItemViewModel For(TodoItemViewModel todo) => new(todo, null);

    public static CaptureItemViewModel For(NoteEntry note) => new(null, note);

    public TodoItemViewModel? Todo { get; }

    public NoteEntry? Note { get; }

    public bool IsTodo => Todo is not null;

    public bool IsNote => Note is not null;

    public string Text => Todo?.Text ?? Note?.Text ?? string.Empty;

    public DateTimeOffset CreatedAt => Todo?.Entry.CreatedAt ?? Note?.CreatedAt ?? default;

    public string TimeText => CreatedAt.ToLocalTime().ToString("h:mm tt");
}
