namespace AudioTunePro.Core.Services;

/// <summary>
/// Runs work on its own background thread so the UI thread never waits on disk, registry or COM audio
/// calls (any of which can stall for seconds, which is what Windows reports as "stopped interacting").
/// Only the newest submitted item matters: if several arrive while the handler is busy, the older ones are
/// dropped, which is exactly right for "apply the current EQ" or "set the volume to where the slider is now".
/// </summary>
public sealed class LatestWinsWorker<T> : IDisposable
{
    private readonly Action<T> _handler;
    private readonly object _gate = new();
    private readonly Thread _thread;
    private T? _pending;
    private bool _hasPending;
    private bool _busy;
    private bool _disposed;

    public LatestWinsWorker(Action<T> handler, string name)
    {
        _handler = handler;
        _thread = new Thread(Run) { IsBackground = true, Name = name };
        _thread.Start();
    }

    /// <summary>Queues an item, replacing any item that has not started yet. Ignored after <see cref="Dispose"/>.</summary>
    public void Submit(T item)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _pending = item;
            _hasPending = true;
            Monitor.PulseAll(_gate);
        }
    }

    /// <summary>Waits until nothing is queued or running. Returns false if that did not happen within the timeout.</summary>
    public bool Flush(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        lock (_gate)
        {
            while (_hasPending || _busy)
            {
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero) return false;
                Monitor.Wait(_gate, remaining);
            }
            return true;
        }
    }

    /// <summary>Stops accepting work; an item already queued is still processed before the thread exits.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            Monitor.PulseAll(_gate);
        }
    }

    private void Run()
    {
        while (true)
        {
            T item;
            lock (_gate)
            {
                while (!_hasPending && !_disposed) Monitor.Wait(_gate);
                if (!_hasPending) return; // disposed and drained

                item = _pending!;
                _pending = default;
                _hasPending = false;
                _busy = true;
            }

            try
            {
                _handler(item);
            }
            catch (Exception ex)
            {
                // A failing job must not kill the worker (later jobs, e.g. the next EQ change, still need it).
                AppLog.Error($"Background job '{_thread.Name}' failed", ex);
            }
            finally
            {
                lock (_gate)
                {
                    _busy = false;
                    Monitor.PulseAll(_gate);
                }
            }
        }
    }
}
