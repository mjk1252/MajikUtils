namespace Dock.Core.Services;

/// <summary>
/// A live read of what the speakers are actually playing, split into frequency bands.
///
/// The island's equalizer bars were a fixed animation -- four sine waves that looked like sound
/// without being it. This is the real thing behind them.
/// </summary>
public interface IAudioLevelSource
{
    /// <summary>
    /// One value per band, 0 (silent) to 1 (as loud as this band has been lately), low frequencies
    /// first. Raised off the UI thread, several dozen times a second.
    /// </summary>
    event EventHandler<double[]>? LevelsChanged;

    /// <summary>How many bands <see cref="LevelsChanged"/> carries.</summary>
    int BandCount { get; }

    /// <summary>
    /// Begins capturing. False means this machine will not give us the audio at all, and whatever
    /// is drawing the levels should fall back to something that does not need them.
    /// </summary>
    bool Start();

    void Stop();
}
