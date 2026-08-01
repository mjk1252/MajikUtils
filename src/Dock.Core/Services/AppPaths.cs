namespace Dock.Core.Services;

/// <summary>
/// Where MajikUtils keeps its state. One place, because the stores used to each build the path
/// themselves and renaming the app meant finding every copy.
/// </summary>
public static class AppPaths
{
    private const string FolderName = "MajikUtils";

    /// <summary>The app was called Dock before; anything it left behind is adopted on first run.</summary>
    private const string LegacyFolderName = "Dock";

    private static readonly Lazy<string> Resolved = new(Resolve);

    public static string DataDirectory => Resolved.Value;

    public static string IconsDirectory => Path.Combine(DataDirectory, "icons");

    public static string CustomIconsDirectory => Path.Combine(IconsDirectory, "custom");

    /// <summary>Not named File -- that would shadow System.IO.File inside this class.</summary>
    public static string FilePath(string name) => Path.Combine(DataDirectory, name);

    private static string Resolve()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var path = Path.Combine(root, FolderName);

        if (!Directory.Exists(path))
        {
            var legacy = Path.Combine(root, LegacyFolderName);
            if (Directory.Exists(legacy))
                TryMigrate(legacy, path);
        }

        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Copies rather than moves: if anything here fails halfway, the old folder is still intact and
    /// the user's shelf and stacks are recoverable by hand. The leftovers are small enough not to
    /// be worth the risk of deleting them.
    /// </summary>
    private static void TryMigrate(string from, string to)
    {
        try
        {
            Directory.CreateDirectory(to);

            foreach (var source in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
            {
                var destination = Path.Combine(to, Path.GetRelativePath(from, source));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination, overwrite: false);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Starting with empty settings beats refusing to start.
        }
    }
}
