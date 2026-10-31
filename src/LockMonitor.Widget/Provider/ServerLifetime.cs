namespace LockMonitor.Provider;

/// <summary>
/// Keeps the COM server process alive while at least one widget is pinned.
/// When the last widget is removed (or none exist at start-up) the process exits after a grace
/// period; the widget host simply re-launches it through COM when it is needed again.
/// </summary>
internal sealed class ServerLifetime(TimeSpan idleGrace) : IDisposable
{
    private readonly ManualResetEventSlim _exit = new(false);
    private readonly Lock _gate = new();
    private Timer? _idleTimer;

    public void OnWidgetCountChanged(int count)
    {
        lock (_gate)
        {
            if (count > 0)
            {
                _idleTimer?.Dispose();
                _idleTimer = null;
            }
            else if (_idleTimer is null)
            {
                Log.Info($"No widgets left; exiting in {idleGrace.TotalSeconds:0}s unless one is created");
                _idleTimer = new Timer(_ => RequestExit("idle"), null, idleGrace, Timeout.InfiniteTimeSpan);
            }
        }
    }

    public void RequestExit(string reason)
    {
        Log.Info($"Exit requested ({reason})");
        _exit.Set();
    }

    public void WaitForExit() => _exit.Wait();

    public void Dispose()
    {
        lock (_gate)
        {
            _idleTimer?.Dispose();
            _idleTimer = null;
        }
        _exit.Dispose();
    }
}
