using System.Text.Json;
using System.Text.Json.Serialization;
using Dock.Core.Models;

namespace Dock.Core.Services;

public sealed class SettingsStore
{
    private readonly string _settingsPath;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public SettingsStore()
        : this(AppPaths.FilePath("settings.json"))
    {
    }

    /// <summary>
    /// Takes the path, for the tests. Every other store here already had this constructor; this one
    /// did not, which is why its behaviour on a fresh install went unasserted for so long.
    /// </summary>
    public SettingsStore(string settingsPath) => _settingsPath = settingsPath;

    public AppSettings Load()
    {
        if (!File.Exists(_settingsPath))
        {
            // Written rather than only returned, so a fresh install has its configuration on disk
            // from the first run instead of after the first setting anybody happens to change.
            //
            // It went unnoticed for a long time because nothing depends on the file existing --
            // right up until a machine that would not behave had no settings to inspect, and the
            // quickest way to give it any was to copy someone else's across. That carries their
            // monitor layout and every other choice with it, which is a worse way to configure a
            // machine than any default.
            var defaults = new AppSettings();
            TrySave(defaults);

            return defaults;
        }

        try
        {
            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_settingsPath, json);
    }

    /// <summary>
    /// Saves, and shrugs if it cannot. Used for the first-run write, where failing is no reason to
    /// refuse to start -- the app ran on defaults with no file at all for its whole life until now.
    /// </summary>
    private void TrySave(AppSettings settings)
    {
        try
        {
            Save(settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
