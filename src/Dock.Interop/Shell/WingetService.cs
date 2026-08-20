using System.Diagnostics;
using System.Text.RegularExpressions;
using Dock.Core.Models;
using Dock.Core.Services;

namespace Dock.Interop.Shell;

/// <summary>
/// Shells out to the winget CLI for search/install. Winget has no stable machine-readable
/// output mode for search, so this parses its aligned console table by locating each column's
/// start offset from the header row (the row directly above the "---" separator) and slicing
/// every data row at those same offsets -- the standard approach for parsing this table.
/// </summary>
public sealed class WingetService : IWingetService
{
    /// <summary>
    /// Runs winget and collects what it said, without deadlocking on its own output.
    ///
    /// Both call sites used to redirect stderr and then never read it. A child process writing more
    /// than the pipe buffer holds -- a few kilobytes -- blocks until somebody drains it, so winget
    /// would stop mid-write, never exit, and the ReadToEnd waiting on *stdout* would wait forever
    /// for an end that could no longer come. Nothing in the app would report it: the search spinner
    /// simply never stopped, and an install's ring never finished turning.
    ///
    /// So stderr is drained on its own task while stdout is read here, and the whole thing is
    /// bounded: a winget that has genuinely wedged gets killed rather than owning a thread until
    /// the app closes.
    /// </summary>
    private static (int ExitCode, string Output)? Run(ProcessStartInfo info, TimeSpan timeout)
    {
        using var process = Process.Start(info);
        if (process is null)
            return null;

        // Started before the synchronous read below, or the two pipes take it in turns to fill.
        var draining = process.StandardError.ReadToEndAsync();

        var output = process.StandardOutput.ReadToEnd();

        if (!process.WaitForExit((int)timeout.TotalMilliseconds))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception)
            {
                // It exited between the check and the kill, or it is not ours to kill. Either way
                // there is nothing further to do about it.
            }

            return null;
        }

        // Best effort: the process is gone, so this has already completed or never will.
        draining.Wait(TimeSpan.FromSeconds(1));

        return (process.ExitCode, output);
    }

    /// <summary>How long a search may take before it is treated as wedged.</summary>
    private static readonly TimeSpan SearchTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// And an install. Generous, because a large package on a slow connection genuinely takes
    /// minutes -- but not unbounded, because the island is showing a ring the whole time and a ring
    /// that never stops is worse than one that stops on a failure.
    /// </summary>
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(30);

    public IReadOnlyList<WingetResult> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        string output;
        try
        {
            var info = new ProcessStartInfo
            {
                FileName = "winget",
                ArgumentList = { "search", query, "--accept-source-agreements", "--disable-interactivity" },
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            if (Run(info, SearchTimeout) is not { } run)
                return [];

            output = run.Output;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            // winget not installed/available -- degrade to no results.
            return [];
        }

        return ParseSearchTable(output);
    }

    private static List<WingetResult> ParseSearchTable(string output)
    {
        var lines = output.Replace("\r\n", "\n").Split('\n');

        var separatorIndex = Array.FindIndex(lines, l => l.Length > 5 && l.TrimEnd().All(c => c == '-'));
        if (separatorIndex <= 0)
            return [];

        var header = lines[separatorIndex - 1];
        var nameCol = 0;
        var idCol = header.IndexOf("Id", StringComparison.Ordinal);
        var versionCol = header.IndexOf("Version", StringComparison.Ordinal);

        if (idCol < 0 || versionCol < 0 || versionCol <= idCol)
            return [];

        var results = new List<WingetResult>();

        for (var i = separatorIndex + 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line) || line.Length <= idCol)
                continue;

            var name = line[nameCol..idCol].Trim();
            var idEnd = Math.Min(versionCol, line.Length);
            var id = line[idCol..idEnd].Trim();

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(id))
                continue;

            results.Add(new WingetResult { Name = name, Id = id });
        }

        return results;
    }

    private static readonly Regex ValidWingetId = new(@"^[A-Za-z0-9_.\-]+$", RegexOptions.Compiled);

    public void Install(WingetResult result, IWingetProgress? report = null)
    {
        // Winget package IDs are always simple tokens (e.g. "Microsoft.VisualStudioCode").
        // Rejecting anything else avoids ever building a shell command line out of an
        // unexpected string, however unlikely a malicious one is to come from winget itself.
        if (!ValidWingetId.IsMatch(result.Id))
            return;

        report?.Progress($"Installing {result.Name}", null);

        try
        {
            var info = new ProcessStartInfo
            {
                FileName = "winget",
                ArgumentList =
                {
                    "install", "--id", result.Id,
                    "--accept-package-agreements", "--accept-source-agreements",

                    // Deliberate, and a real trade. There is no console for winget to prompt into
                    // now, so a package whose installer wants an answer would hang forever waiting
                    // for one nobody can give. Failing fast and saying so is the better of the two
                    // bad outcomes; the package can still be installed from a terminal by hand.
                    // (Elevation is unaffected -- a UAC prompt comes from the installer itself and
                    // appears on the secure desktop whether or not we made a window.)
                    "--disable-interactivity"
                },
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,

                // Same reason as ProcessAppLauncher: a process left running in our own install
                // folder pins the directory Velopack has to rename to apply an update.
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            };

            if (Run(info, InstallTimeout) is not { } run)
            {
                report?.Finished($"{result.Name} could not be installed", succeeded: false);
                return;
            }

            var ok = run.ExitCode == 0;

            report?.Finished(
                ok ? $"Installed {result.Name}" : $"{result.Name} could not be installed",
                ok);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            report?.Finished("winget is not available", succeeded: false);
        }
    }
}
