using Dock.Core.Models;

namespace Dock.Core.Services;

public interface IWingetService
{
    IReadOnlyList<WingetResult> Search(string query);

    /// <summary>
    /// Installs a package.
    ///
    /// <paramref name="report"/> is called as the install moves, and once more when it stops. It is
    /// how the island shows an install happening at all: this used to open a console window and
    /// leave the app with nothing to say about it, which meant a two-minute install looked exactly
    /// like a click that did nothing.
    ///
    /// Called from a background thread. Marshalling is the caller's business, since only the caller
    /// knows what it needs marshalling to.
    /// </summary>
    void Install(WingetResult result, IWingetProgress? report = null);
}

/// <summary>
/// What an install tells the world about itself while it runs. An interface rather than a pair of
/// callbacks so that the two halves cannot be wired up separately and end up disagreeing about
/// which install they belong to.
/// </summary>
public interface IWingetProgress
{
    /// <summary>Still going. <paramref name="fraction"/> is null while there is no number to give.</summary>
    void Progress(string label, double? fraction);

    /// <summary>Stopped, for better or worse.</summary>
    void Finished(string label, bool succeeded);
}
