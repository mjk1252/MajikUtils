namespace Dock.Interop.Windowing;

/// <summary>
/// Tracks whether Dock.App last hid the taskbar without a confirmed clean exit, so a watchdog
/// (or the next launch) knows to force the taskbar back rather than leaving the user stranded.
/// </summary>
public static class TaskbarSafety
{
    public readonly record struct TaskbarPosition(long Handle, int Left, int Top);

    public static string FlagPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dock", "taskbar-hidden.flag");

    private static string PositionsPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dock", "taskbar-positions.txt");

    public static void SavePositions(IReadOnlyList<TaskbarPosition> positions)
    {
        var dir = Path.GetDirectoryName(PositionsPath)!;
        Directory.CreateDirectory(dir);
        var lines = positions.Select(p => $"{p.Handle},{p.Left},{p.Top}");
        File.WriteAllLines(PositionsPath, lines);
    }

    public static IReadOnlyList<TaskbarPosition> LoadPositions()
    {
        if (!File.Exists(PositionsPath))
            return [];

        var result = new List<TaskbarPosition>();
        foreach (var line in File.ReadAllLines(PositionsPath))
        {
            var parts = line.Split(',');
            if (parts.Length == 3 &&
                long.TryParse(parts[0], out var handle) &&
                int.TryParse(parts[1], out var left) &&
                int.TryParse(parts[2], out var top))
            {
                result.Add(new TaskbarPosition(handle, left, top));
            }
        }

        return result;
    }

    public static void ClearPositions()
    {
        try
        {
            File.Delete(PositionsPath);
        }
        catch (IOException)
        {
            // Best effort.
        }
    }

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
