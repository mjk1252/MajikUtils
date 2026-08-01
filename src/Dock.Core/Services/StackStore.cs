using System.Text.Json;
using Dock.Core.Models;

namespace Dock.Core.Services;

public sealed class StackStore
{
    private readonly string _stacksPath;

    public StackStore()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dock");
        Directory.CreateDirectory(dir);
        _stacksPath = Path.Combine(dir, "stacks.json");
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
