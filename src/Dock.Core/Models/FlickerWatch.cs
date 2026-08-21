namespace Dock.Core.Models;

/// <summary>
/// Notices when the island is changing state far more often than anything a person did could
/// explain, and says what drove each change.
///
/// This exists because of a fault that could not be reproduced where it could be looked at. The
/// island was reported flickering open and closed on a machine nobody debugging it had access to,
/// with the pointer nowhere near it -- and reading the code produced four plausible causes and no
/// way to tell which, if any, was the real one. Guessing from a symptom description is how a whole
/// afternoon gets spent fixing things that were not broken.
///
/// So the app diagnoses itself. Every show, expand and pin transition is offered here with the
/// reason it happened; when they arrive faster than a person could be causing them, one entry goes
/// to the log naming the last several and what asked for them. Costs a timestamp comparison in the
/// ordinary case, and turns "it flickers sometimes" into a file that can be read.
///
/// In <c>Dock.Core</c> and not the window, because it is a rule about timing rather than anything
/// to do with WPF, and because it can then be tested by walking a clock rather than by trying to
/// make a real island misbehave.
/// </summary>
public sealed class FlickerWatch
{
    /// <summary>
    /// How many transitions inside <see cref="Window"/> count as a flicker rather than as use.
    ///
    /// Six is comfortably above anything deliberate. Opening the island, pinning it, typing and
    /// closing it is four transitions and takes seconds; six inside three seconds is not a person.
    /// </summary>
    public const int Threshold = 6;

    /// <summary>The span the transitions have to fall inside to count together.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(3);

    /// <summary>
    /// How long to stay quiet after reporting. A flicker does not stop because it was written
    /// down, and a log filling at ten lines a second helps nobody -- the first report already has
    /// everything the later ones would say.
    /// </summary>
    public static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(5);

    private readonly List<(DateTimeOffset At, string Reason)> _recent = [];

    private DateTimeOffset? _lastReport;

    /// <summary>
    /// Offers one transition. Returns the text to write down when this is the one that tips a
    /// flicker into being worth reporting, and null every other time -- which is almost always.
    /// </summary>
    /// <param name="reason">What asked for the change: the caller's own name for itself, like
    /// "pointer left" or "activity ended". This is the whole value of the report.</param>
    public string? Record(DateTimeOffset now, string reason)
    {
        _recent.Add((now, reason));

        // Only the window's worth matters. Trimming from the front keeps this a handful of entries
        // however long the app has been running.
        var cutoff = now - Window;
        while (_recent.Count > 0 && _recent[0].At < cutoff)
            _recent.RemoveAt(0);

        if (_recent.Count < Threshold)
            return null;

        if (_lastReport is { } last && now - last < Cooldown)
            return null;

        _lastReport = now;

        var counts = _recent
            .GroupBy(r => r.Reason)
            .OrderByDescending(g => g.Count())
            .Select(g => $"{g.Key} x{g.Count()}");

        var report =
            $"The island changed state {_recent.Count} times in {Window.TotalSeconds:0} seconds, " +
            $"which no deliberate use accounts for. Reasons: {string.Join(", ", counts)}. " +
            $"Sequence: {string.Join(" -> ", _recent.Select(r => r.Reason))}";

        // Cleared, so the next report describes the next burst rather than this one again.
        _recent.Clear();

        return report;
    }
}
