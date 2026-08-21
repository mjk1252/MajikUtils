namespace Dock.Core.Services;

/// <summary>
/// Where MajikUtils keeps its state. One place, because the stores used to each build the path
/// themselves and renaming the app meant finding every copy.
///
/// That place is <c>%LocalAppData%\Majik\MajikUtils</c>, and the vendor folder in the middle is
/// load-bearing rather than tidiness. State used to live in <c>%LocalAppData%\MajikUtils</c> --
/// which is the exact directory Velopack installs the application into. A delta update leaves the
/// contents alone, so it worked for months; a full update cleans the directory first, and took
/// every note, todo, stack, shelf entry, pinned clipboard item and setting with it. Nothing was
/// recycled, because an installer tidying its own install directory has no reason to think it is
/// deleting user data.
///
/// So the rule this file exists to enforce: the data directory must never be one an installer
/// believes it owns. It is a sibling of the install directory now, not the same folder.
/// </summary>
public static class AppPaths
{
    /// <summary>
    /// The folder the data directory lives *inside*, which is the whole point: Velopack installs to
    /// <c>%LocalAppData%\MajikUtils</c>, and nothing it does reaches into a different top-level
    /// folder.
    /// </summary>
    private const string VendorFolderName = "Majik";

    private const string FolderName = "MajikUtils";

    /// <summary>The app was called Dock before; anything it left behind is adopted on first run.</summary>
    private const string LegacyFolderName = "Dock";

    /// <summary>
    /// Every file the app owns, and the whole of what a migration copies.
    ///
    /// A list rather than "everything in the folder", because one of the places being migrated
    /// *from* is the install directory, and copying everything there would drag two hundred
    /// megabytes of runtime DLLs along with the settings.
    /// </summary>
    public static readonly string[] DataFileNames =
    [
        "settings.json",
        "notes.json",
        "todos.json",
        "shelf.json",
        "stacks.json",
        "clipboard-pinned.json",
        "crash.log"
    ];

    /// <summary>Subfolders the app owns. Copied whole, contents and all.</summary>
    public static readonly string[] DataDirectoryNames = ["icons"];

    private static readonly Lazy<string> Resolved = new(Resolve);

    public static string DataDirectory => Resolved.Value;

    public static string IconsDirectory => Path.Combine(DataDirectory, "icons");

    public static string CustomIconsDirectory => Path.Combine(IconsDirectory, "custom");

    /// <summary>Not named File -- that would shadow System.IO.File inside this class.</summary>
    public static string FilePath(string name) => Path.Combine(DataDirectory, name);

    private static string Resolve()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var path = Path.Combine(root, VendorFolderName, FolderName);

        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);

            // Newest first, and copies never overwrite -- so where the same file exists in both, the
            // one from the more recent layout is the one that survives.
            AdoptDataFrom(Path.Combine(root, FolderName), path);
            AdoptDataFrom(Path.Combine(root, LegacyFolderName), path);
        }

        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Copies whatever of the app's own state is in <paramref name="from"/> into
    /// <paramref name="to"/>, leaving anything else where it is.
    ///
    /// Copies rather than moves, and never overwrites. If this fails halfway the old folder is
    /// still intact and everything is recoverable by hand -- which matters more here than usual,
    /// given what this method exists because of. One of the folders it reads is an install
    /// directory that an updater may delete without warning, so leaving the original in place costs
    /// nothing and taking it costs everything.
    /// </summary>
    public static void AdoptDataFrom(string from, string to)
    {
        if (!Directory.Exists(from) ||
            string.Equals(Path.GetFullPath(from), Path.GetFullPath(to), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(to);

            foreach (var name in DataFileNames)
            {
                var source = Path.Combine(from, name);
                var destination = Path.Combine(to, name);

                if (File.Exists(source) && !File.Exists(destination))
                    File.Copy(source, destination);
            }

            foreach (var name in DataDirectoryNames)
            {
                var sourceDirectory = Path.Combine(from, name);
                if (!Directory.Exists(sourceDirectory))
                    continue;

                foreach (var source in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
                {
                    var destination = Path.Combine(to, name, Path.GetRelativePath(sourceDirectory, source));

                    if (File.Exists(destination))
                        continue;

                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(source, destination);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Starting with empty settings beats refusing to start.
        }
    }
}
