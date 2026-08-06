using System.Text.Json;
using Dock.Core.Models;

namespace Dock.Core.Services;

public sealed class TodosStore
{
    private readonly string _todosPath;

    public TodosStore() : this(AppPaths.FilePath("todos.json"))
    {
    }

    /// <summary>Lets tests point at a scratch file instead of the real app data directory.</summary>
    public TodosStore(string todosPath)
    {
        _todosPath = todosPath;
    }

    public List<TodoEntry> Load()
    {
        if (!File.Exists(_todosPath))
            return [];

        try
        {
            var json = File.ReadAllText(_todosPath);
            return JsonSerializer.Deserialize<List<TodoEntry>>(json) ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public void Save(List<TodoEntry> todos)
    {
        var json = JsonSerializer.Serialize(todos, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_todosPath, json);
    }
}
