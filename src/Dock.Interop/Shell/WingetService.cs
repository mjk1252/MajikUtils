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

            using var process = Process.Start(info);
            if (process is null)
                return [];

            output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(15000);
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

    public void Install(WingetResult result)
    {
        // Winget package IDs are always simple tokens (e.g. "Microsoft.VisualStudioCode").
        // Rejecting anything else avoids ever building a shell command line out of an
        // unexpected string, however unlikely a malicious one is to come from winget itself.
        if (!ValidWingetId.IsMatch(result.Id))
            return;

        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            ArgumentList = { "/k", "winget", "install", "--id", result.Id, "--accept-package-agreements", "--accept-source-agreements" },
            UseShellExecute = true,

            // Same reason as ProcessAppLauncher: a console left open in our own install folder
            // pins the directory Velopack has to rename to apply an update.
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        });
    }
}
