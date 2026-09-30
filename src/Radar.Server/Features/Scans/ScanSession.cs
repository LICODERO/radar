using System.Runtime.CompilerServices;
using Radar.Scanner;

namespace Radar.Server;

/// <summary>One scan run. Keeps the full event history so a late SSE subscriber still gets every event.</summary>
public sealed class ScanSession
{
    private readonly object _lock = new();
    private readonly List<ScanEvent> _events = [];
    private TaskCompletionSource _signal = NewSignal();
    private bool _finished;

    public string Id { get; } = Guid.NewGuid().ToString("N");
    public string Root { get; }
    public CancellationTokenSource Cts { get; } = new();

    public ScanSession(string root) => Root = root;

    public bool Finished { get { lock (_lock) return _finished; } }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Append(ScanEvent ev)
    {
        lock (_lock)
        {
            _events.Add(ev);
            var old = _signal;
            _signal = NewSignal();
            old.TrySetResult();
        }
    }

    public void Complete()
    {
        lock (_lock)
        {
            _finished = true;
            var old = _signal;
            _signal = NewSignal();
            old.TrySetResult();
        }
    }

    public async IAsyncEnumerable<ScanEvent> Subscribe([EnumeratorCancellation] CancellationToken ct = default)
    {
        var i = 0;
        while (true)
        {
            ScanEvent? next = null;
            bool finished;
            Task wait;
            lock (_lock)
            {
                if (i < _events.Count) next = _events[i++];
                finished = _finished;
                wait = _signal.Task;
            }
            if (next is not null) { yield return next; continue; }
            if (finished) yield break;
            await wait.WaitAsync(ct);
        }
    }
}

/// <summary>Runs at most one scan at a time and persists the finished result.</summary>
public sealed class ScanManager(IScanStore store, LatestScanCache cache, ILogger<ScanManager> log)
{
    private readonly object _lock = new();
    private ScanSession? _current;

    public ScanSession? Get(string id) { lock (_lock) return _current?.Id == id ? _current : null; }

    public ScanSession? Running { get { lock (_lock) return _current is { Finished: false } ? _current : null; } }

    /// <summary>Starts a scan, or returns the one already running.</summary>
    public (ScanSession Session, bool Started) Start(string root, int maxDepth)
    {
        lock (_lock)
        {
            if (_current is { Finished: false }) return (_current, false);
            var session = new ScanSession(root);
            _current = session;
            _ = Task.Run(() => Run(session, maxDepth));
            return (session, true);
        }
    }

    public bool Cancel(string id)
    {
        var s = Get(id);
        if (s is null || s.Finished) return false;
        s.Cts.Cancel();
        return true;
    }

    private async Task Run(ScanSession session, int maxDepth)
    {
        try
        {
            var progress = new DirectProgress(e =>
            {
                // "completed" is emitted only after the result is saved, so the UI can fetch it right away
                if (e is not ScanCompleted) session.Append(e);
            });
            var result = new RadarScanner().Scan(new ScanOptions(session.Root, maxDepth), progress, session.Cts.Token);
            await store.SaveAsync(result);
            cache.Set(result);
            session.Append(new ScanCompleted(result.DurationMs, result.Summary));
        }
        catch (OperationCanceledException)
        {
            session.Append(new ScanCancelled());
        }
        catch (Exception e)
        {
            log.LogError(e, "Scan of {Root} failed", session.Root);
            session.Append(new ScanFailed(e.Message));
        }
        finally
        {
            session.Complete();
        }
    }

    private sealed class DirectProgress(Action<ScanEvent> on) : IProgress<ScanEvent>
    {
        public void Report(ScanEvent value) => on(value);
    }
}
