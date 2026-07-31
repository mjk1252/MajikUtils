using System.Text.Json;
using Dock.Core.Models;

namespace Dock.Core.Services;

public sealed class ConfigStore
{
    private readonly string _configPath;

    public ConfigStore()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dock");
        Directory.CreateDirectory(dir);
        _configPath = Path.Combine(dir, "config.json");
    }

    public DockConfig Load()
    {
        if (!File.Exists(_configPath))
            return CreateDefault();

        try
        {
            var json = File.ReadAllText(_configPath);
            var config = JsonSerializer.Deserialize<DockConfig>(json);
            return config ?? CreateDefault();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return CreateDefault();
        }
    }

    public void Save(DockConfig config)
    {
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_configPath, json);
    }

    private static DockConfig CreateDefault()
    {
        var system32 = Environment.SystemDirectory;
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        return new DockConfig
        {
            PinnedApps =
            [
                new PinnedApp
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "File Explorer",
                    ExecutablePath = Path.Combine(windows, "explorer.exe")
                },
                new PinnedApp
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "Notepad",
                    ExecutablePath = Path.Combine(system32, "notepad.exe")
                },
                new PinnedApp
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "Calculator",
                    ExecutablePath = Path.Combine(system32, "calc.exe")
                },
                new PinnedApp
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "Settings",
                    ExecutablePath = Path.Combine(windows, "ImmersiveControlPanel", "SystemSettings.exe"),
                    Arguments = null
                },
            ]
        };
    }
}
