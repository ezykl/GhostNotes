using System;
using System.Threading;
using Timer = System.Threading.Timer;

namespace GhostNotes.Services;

public sealed class AutosaveScheduler : IDisposable
{
    private readonly object _sync = new();
    private readonly Action _flush;
    private readonly TimeSpan _delay;
    private Timer? _timer;
    private bool _pending;
    private bool _disposed;

    public AutosaveScheduler(Action flush, TimeSpan delay)
    {
        _flush = flush;
        _delay = delay;
        _timer = new Timer(_ => FlushOnce(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public void Trigger()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _pending = true;
            _timer?.Change(_delay, Timeout.InfiniteTimeSpan);
        }
    }

    public void FlushNow()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
        FlushOnce();
    }

    public void Cancel()
    {
        lock (_sync)
        {
            _pending = false;
            _timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _timer?.Dispose();
            _timer = null;
        }
        FlushOnce();
        lock (_sync) { _disposed = true; }
    }

    private void FlushOnce()
    {
        bool run;
        lock (_sync) { run = _pending; _pending = false; }
        if (run) _flush();
    }
}
