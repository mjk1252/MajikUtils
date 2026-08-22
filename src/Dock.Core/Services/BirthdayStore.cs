using Dock.Core.Models;

namespace Dock.Core.Services;

/// <summary>
/// The birthday list on disk, and the watch that notices somebody editing it.
///
/// The only store here whose file is meant to be opened by something other than MajikUtils, which
/// is what the watcher is for: notes and todos change because the app changed them, and this one
/// changes because a spreadsheet saved over it. Without the watch, adding a birthday would take
/// effect at the next restart -- and the first thing anybody does after adding today's birthday is
/// look at the island to see whether it worked.
/// </summary>
public sealed class BirthdayStore : IDisposable
{
    private readonly string _path;
    private FileSystemWatcher? _watcher;

    /// <summary>
    /// Coalesces the burst a single save produces. An editor writing a file raises anything from
    /// one event to four (write, truncate, rename-over, attributes), and re-reading on each of them
    /// means reading a half-written file at least once.
    /// </summary>
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(400);

    private CancellationTokenSource? _settling;

    /// <summary>
    /// Raised, off the UI thread, once an external edit has settled. Carries nothing: the list is
    /// small and the handler is going to call <see cref="Load"/> anyway.
    /// </summary>
    public event Action? Changed;

    public BirthdayStore() : this(AppPaths.FilePath("birthdays.csv"))
    {
    }

    /// <summary>Lets tests point at a scratch file instead of the real app data directory.</summary>
    public BirthdayStore(string path) => _path = path;

    /// <summary>Where the file is, for the menu entry that opens it.</summary>
    public string Path => _path;

    /// <summary>
    /// Creates the file with its header and one example if it is not there yet.
    ///
    /// Called before the file is ever opened for editing, because "Edit birthdays..." opening a
    /// blank document -- or an editor's "this file does not exist" box -- is a worse answer than a
    /// commented example showing exactly what to type.
    /// </summary>
    public void EnsureExists()
    {
        try
        {
            if (!File.Exists(_path))
                File.WriteAllText(_path, BirthdayCsv.Template);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An unwritable data directory is not worth failing over: everything downstream of
            // here already treats a missing list as an empty one.
        }
    }

    /// <summary>
    /// Everything in the file, valid lines only. A missing or unreadable file is an empty list --
    /// there is no failure mode here worth telling anybody about.
    /// </summary>
    public List<Birthday> Load()
    {
        try
        {
            return File.Exists(_path) ? BirthdayCsv.Parse(File.ReadAllText(_path)) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// Rewrites the file. Only reached by adding somebody from inside the app -- a hand-edited file
    /// is never rewritten, since reformatting a list the user is maintaining is not the app's to do.
    /// </summary>
    public void Save(IEnumerable<Birthday> birthdays)
    {
        File.WriteAllText(_path, BirthdayCsv.Format(birthdays));
    }

    /// <summary>
    /// Starts watching the file for edits made outside the app.
    ///
    /// Watches the *directory* rather than the file, because most editors save by writing a
    /// temporary file and renaming it over the original -- which destroys the handle a file-scoped
    /// watch is holding, and leaves the watcher silently pointed at a file that no longer exists.
    /// </summary>
    public void StartWatching()
    {
        if (_watcher is not null)
            return;

        var directory = System.IO.Path.GetDirectoryName(_path);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            return;

        try
        {
            _watcher = new FileSystemWatcher(directory, System.IO.Path.GetFileName(_path))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = true
            };

            _watcher.Changed += OnFileTouched;
            _watcher.Created += OnFileTouched;
            _watcher.Renamed += OnFileTouched;
            _watcher.Deleted += OnFileTouched;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // No watch is a feature working slightly worse -- the list still loads at startup.
            _watcher = null;
        }
    }

    /// <summary>
    /// Restarts the settle timer on every event and fires once it goes quiet, so one save produces
    /// one reload however many events the editor happened to raise.
    /// </summary>
    private void OnFileTouched(object sender, FileSystemEventArgs e)
    {
        _settling?.Cancel();
        _settling?.Dispose();

        var settling = new CancellationTokenSource();
        _settling = settling;

        _ = Task.Delay(SettleDelay, settling.Token).ContinueWith(task =>
        {
            if (task.IsCompletedSuccessfully)
                Changed?.Invoke();
        }, TaskScheduler.Default);
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _watcher = null;

        _settling?.Cancel();
        _settling?.Dispose();
        _settling = null;
    }
}
