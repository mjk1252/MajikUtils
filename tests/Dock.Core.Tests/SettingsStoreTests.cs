using Dock.Core.Models;
using Dock.Core.Services;

namespace Dock.Core.Tests;

/// <summary>
/// The store used to return defaults without ever writing them, so a fresh install had no
/// settings file at all until somebody happened to change a setting. Nothing depended on the file
/// existing, which is why it went unnoticed -- right up until a machine that would not behave had
/// no settings to inspect, and the quickest fix on hand was to copy another machine's file across,
/// carrying its monitor layout and every other choice with it.
/// </summary>
public class SettingsStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"majikutils-settings-{Guid.NewGuid():N}");

    private readonly string _path;

    public SettingsStoreTests()
    {
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "settings.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Load_OnAFreshInstall_WritesTheDefaultsDown()
    {
        var store = new SettingsStore(_path);

        var settings = store.Load();

        Assert.True(File.Exists(_path));
        Assert.True(settings.ShowMediaIsland);

        // And what was written is what was returned, rather than an empty file.
        Assert.True(new SettingsStore(_path).Load().ShowMediaIsland);
    }

    [Fact]
    public void Load_KeepsWhatIsAlreadyThere()
    {
        var store = new SettingsStore(_path);
        var settings = store.Load();

        settings.ShowClock = false;
        settings.IslandShape = IslandShape.Pill;
        store.Save(settings);

        var reloaded = new SettingsStore(_path).Load();

        Assert.False(reloaded.ShowClock);
        Assert.Equal(IslandShape.Pill, reloaded.IslandShape);
    }

    /// <summary>
    /// A settings file that cannot be parsed is not a reason to refuse to start, and never has
    /// been. The defaults stand in and the unreadable file is left alone rather than overwritten,
    /// since it is the only copy of whatever the user had chosen.
    /// </summary>
    [Fact]
    public void Load_FallsBackToDefaultsOnAnUnreadableFile()
    {
        File.WriteAllText(_path, "{ this is not json");

        var settings = new SettingsStore(_path).Load();

        Assert.True(settings.ShowMediaIsland);
        Assert.Equal("{ this is not json", File.ReadAllText(_path));
    }

    /// <summary>A first run that cannot write must still start.</summary>
    [Fact]
    public void Load_StartsEvenWhenTheFileCannotBeWritten()
    {
        var store = new SettingsStore(Path.Combine(_directory, "no-such-folder", "settings.json"));

        var exception = Record.Exception(() => store.Load());

        Assert.Null(exception);
    }
}
