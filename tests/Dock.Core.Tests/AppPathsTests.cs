using Dock.Core.Services;

namespace Dock.Core.Tests;

/// <summary>
/// The migration that moves state out of the install directory.
///
/// These exist because of a data loss, not a hypothetical one. State used to live in
/// %LocalAppData%\MajikUtils, which is the directory Velopack installs into; a full update cleans
/// that directory before writing, and every note, todo, stack, shelf entry and setting went with
/// it, unrecycled. The two properties asserted hardest here are the two that would have prevented
/// it: copy, never move, and copy only what the app owns.
/// </summary>
public class AppPathsTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"majikutils-paths-{Guid.NewGuid():N}");

    private string From => Path.Combine(_root, "from");
    private string To => Path.Combine(_root, "to");

    public AppPathsTests()
    {
        Directory.CreateDirectory(From);
        Directory.CreateDirectory(To);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private void Write(string relative, string content)
    {
        var path = Path.Combine(From, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [Fact]
    public void AdoptDataFrom_BringsEveryFileTheAppOwns()
    {
        foreach (var name in AppPaths.DataFileNames)
            Write(name, $"contents of {name}");

        AppPaths.AdoptDataFrom(From, To);

        foreach (var name in AppPaths.DataFileNames)
            Assert.Equal($"contents of {name}", File.ReadAllText(Path.Combine(To, name)));
    }

    /// <summary>
    /// The one that matters most. One of the folders this reads is the install directory, and a
    /// migration that copied everything in it would drag two hundred megabytes of runtime DLLs
    /// along with the settings.
    /// </summary>
    [Fact]
    public void AdoptDataFrom_LeavesEverythingElseBehind()
    {
        Write("settings.json", "{}");
        Write("MajikUtils.exe", "not data");
        Write("Update.exe", "not data");
        Write("current/System.Private.CoreLib.dll", "not data");
        Write("packages/MajikUtils-2.7.0-full.nupkg", "not data");

        AppPaths.AdoptDataFrom(From, To);

        Assert.True(File.Exists(Path.Combine(To, "settings.json")));
        Assert.False(File.Exists(Path.Combine(To, "MajikUtils.exe")));
        Assert.False(File.Exists(Path.Combine(To, "Update.exe")));
        Assert.False(Directory.Exists(Path.Combine(To, "current")));
        Assert.False(Directory.Exists(Path.Combine(To, "packages")));
    }

    /// <summary>
    /// Copy, never move. The source is a directory an updater may delete without warning, so
    /// leaving the original in place costs nothing and taking it costs everything.
    /// </summary>
    [Fact]
    public void AdoptDataFrom_LeavesTheOriginalIntact()
    {
        Write("notes.json", "my notes");

        AppPaths.AdoptDataFrom(From, To);

        Assert.Equal("my notes", File.ReadAllText(Path.Combine(From, "notes.json")));
    }

    [Fact]
    public void AdoptDataFrom_BringsTheIconsFolderWholeAndNested()
    {
        Write("icons/panel.png", "an icon");
        Write("icons/custom/mine.png", "a custom icon");

        AppPaths.AdoptDataFrom(From, To);

        Assert.Equal("an icon", File.ReadAllText(Path.Combine(To, "icons", "panel.png")));
        Assert.Equal("a custom icon", File.ReadAllText(Path.Combine(To, "icons", "custom", "mine.png")));
    }

    /// <summary>
    /// Two layouts are adopted in turn, newest first, so a file already taken from the newer one
    /// must not then be replaced by the older one's copy of it.
    /// </summary>
    [Fact]
    public void AdoptDataFrom_NeverOverwritesWhatIsAlreadyThere()
    {
        File.WriteAllText(Path.Combine(To, "settings.json"), "the newer one");
        Write("settings.json", "the older one");

        AppPaths.AdoptDataFrom(From, To);

        Assert.Equal("the newer one", File.ReadAllText(Path.Combine(To, "settings.json")));
    }

    [Fact]
    public void AdoptDataFrom_DoesNothingWhenThereIsNothingToAdopt()
    {
        var exception = Record.Exception(() =>
            AppPaths.AdoptDataFrom(Path.Combine(_root, "never-existed"), To));

        Assert.Null(exception);
        Assert.Empty(Directory.GetFiles(To));
    }

    /// <summary>A folder asked to adopt from itself must not walk over its own contents.</summary>
    [Fact]
    public void AdoptDataFrom_IgnoresAMigrationOntoItself()
    {
        Write("settings.json", "mine");

        var exception = Record.Exception(() => AppPaths.AdoptDataFrom(From, From));

        Assert.Null(exception);
        Assert.Equal("mine", File.ReadAllText(Path.Combine(From, "settings.json")));
    }

    /// <summary>
    /// The data directory must never be one an installer believes it owns. Velopack installs to
    /// %LocalAppData%\MajikUtils, so anything at or under that path is the bug coming back.
    /// </summary>
    [Fact]
    public void DataDirectory_IsNotInsideTheInstallDirectory()
    {
        var install = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MajikUtils");

        var data = Path.GetFullPath(AppPaths.DataDirectory);

        Assert.False(
            data.StartsWith(Path.GetFullPath(install) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(data, Path.GetFullPath(install), StringComparison.OrdinalIgnoreCase),
            $"Data directory '{data}' is inside the Velopack install directory '{install}'.");
    }
}
