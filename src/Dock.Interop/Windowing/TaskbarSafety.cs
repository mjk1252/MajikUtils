namespace Dock.Interop.Windowing;

/// <summary>
/// Tracks whether Dock.App last hid the taskbar without a confirmed clean exit, so a watchdog
/// (or the next launch) knows to force the taskbar back rather than leaving the user stranded.
/// </summary>
public static class TaskbarSafety
{
    public static string FlagPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dock", "taskbar-hidden.flag");

    public static void MarkHidden()
    {
        var dir = Path.GetDirectoryName(FlagPath)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(FlagPath, DateTime.UtcNow.ToString("O"));
    }

    public static void ClearFlag()
    {
        try
        {
            File.Delete(FlagPath);
        }
        catch (IOException)
        {
            // Best effort; a stale flag just triggers another harmless restore later.
        }
    }

    public static bool IsFlagged() => File.Exists(FlagPath);
}
