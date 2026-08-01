using System.Text.Json;
using Dock.Core.Models;

namespace Dock.Core.Services;

public sealed class ShelfStore
{
    private readonly string _shelfPath;

    public ShelfStore()
    {
        _shelfPath = AppPaths.FilePath("shelf.json");
    }

    public List<ShelfItem> Load()
    {
        if (!File.Exists(_shelfPath))
            return [];

        try
        {
            var json = File.ReadAllText(_shelfPath);
            return JsonSerializer.Deserialize<List<ShelfItem>>(json) ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public void Save(List<ShelfItem> items)
    {
        var json = JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_shelfPath, json);
    }
}
