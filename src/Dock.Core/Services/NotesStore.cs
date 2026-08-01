using System.Text.Json;
using Dock.Core.Models;

namespace Dock.Core.Services;

public sealed class NotesStore
{
    private readonly string _notesPath;

    public NotesStore() : this(AppPaths.FilePath("notes.json"))
    {
    }

    /// <summary>Lets tests point at a scratch file instead of the real app data directory.</summary>
    public NotesStore(string notesPath)
    {
        _notesPath = notesPath;
    }

    public List<NoteEntry> Load()
    {
        if (!File.Exists(_notesPath))
            return [];

        try
        {
            var json = File.ReadAllText(_notesPath);
            return JsonSerializer.Deserialize<List<NoteEntry>>(json) ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public void Save(List<NoteEntry> notes)
    {
        var json = JsonSerializer.Serialize(notes, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_notesPath, json);
    }
}
