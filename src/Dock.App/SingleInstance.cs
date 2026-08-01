using System.IO;
using System.IO.Pipes;

namespace Dock.App;

/// <summary>
/// Keeps one MajikUtils process alive and routes every later launch into it.
///
/// This exists because of pinning: a pinned taskbar button relaunches the exe with
/// "--panel &lt;name&gt;" rather than talking to the running app, so without this a second click on a
/// pinned button would start a whole second copy -- two more taskbar buttons, a second clipboard
/// listener, a duplicate hotkey registration that fails.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Global\MajikUtils.SingleInstance";
    private const string PipeName = "MajikUtils.Panel";

    private readonly Mutex _mutex;
    private CancellationTokenSource? _listenerCancellation;

    public bool IsFirstInstance { get; }

    public SingleInstance()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        IsFirstInstance = createdNew;
    }

    /// <summary>
    /// Hands <paramref name="panel"/> to the already-running instance. Returns false if nothing
    /// was listening, in which case the caller should carry on and start normally rather than
    /// exiting into nowhere -- a stale mutex outliving a crashed process would otherwise make
    /// Dock unlaunchable.
    /// </summary>
    public static bool SendToRunningInstance(string panel)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(2000);

            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine(panel);
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Starts accepting relaunch requests. <paramref name="onPanelRequested"/> is invoked on a
    /// background thread; callers are responsible for marshalling to the UI.
    /// </summary>
    public void StartListening(Action<string> onPanelRequested)
    {
        _listenerCancellation = new CancellationTokenSource();
        var token = _listenerCancellation.Token;

        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(token);

                    using var reader = new StreamReader(server);
                    if (await reader.ReadLineAsync(token) is { } panel && !string.IsNullOrWhiteSpace(panel))
                        onPanelRequested(panel.Trim());
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (IOException)
                {
                    // A client that connected and vanished mid-write. Loop round for the next one.
                }
            }
        }, token);
    }

    public void Dispose()
    {
        _listenerCancellation?.Cancel();
        _listenerCancellation?.Dispose();

        if (IsFirstInstance)
            _mutex.ReleaseMutex();

        _mutex.Dispose();
    }
}
