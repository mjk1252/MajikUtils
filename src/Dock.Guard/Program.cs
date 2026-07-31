using System.Diagnostics;
using Dock.Interop.Windowing;

if (args.Length == 0 || !int.TryParse(args[0], out var parentPid))
    return;

try
{
    var parent = Process.GetProcessById(parentPid);
    parent.WaitForExit();
}
catch (ArgumentException)
{
    // Parent process was already gone by the time we looked it up -- treat as an abnormal
    // exit below, same as if WaitForExit had returned after a crash.
}

var flagPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dock", "taskbar-hidden.flag");

if (File.Exists(flagPath))
{
    // Dock.App exited (or crashed) without clearing the flag, meaning the taskbar may still
    // be hidden. Force it back so the user is never left without a taskbar.
    TaskbarController.Show();

    try
    {
        File.Delete(flagPath);
    }
    catch (IOException)
    {
        // Best effort; a stale flag just triggers another harmless restore on next launch.
    }
}
