using System.Text.Json;
using Dock.Core.Models;

namespace Dock.Core.Services;

public sealed class StackStore
{
    private readonly string _stacksPath;

    public StackStore()
    {
        _stacksPath = AppPaths.FilePath("stacks.json");
    }

    public List<StackFolder> Load()
    {
        if (!File.Exists(_stacksPath))
            return [];

        try
        {
            var json = File.ReadAllText(_stacksPath);
            return JsonSerializer.Deserialize<List<StackFolder>>(json) ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public void Save(List<StackFolder> items)
    {
        var json = JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_stacksPath, json);
    }
}
