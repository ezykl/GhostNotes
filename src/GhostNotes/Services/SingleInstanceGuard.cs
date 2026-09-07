using System;
using System.Threading;

namespace GhostNotes.Services;

public sealed class SingleInstanceGuard : IDisposable
{
    public const string DefaultMutexName = "GhostNotes_SingleInstance_Mutex";
    public const string DefaultSignalName = "GhostNotes_ShowSignal";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle? _signal;
    private bool _disposed;

    public bool IsFirst { get; }

    public SingleInstanceGuard(string mutexName, string signalName = DefaultSignalName)
    {
        _mutex = new Mutex(true, mutexName, out bool createdNew);
        IsFirst = createdNew;
        _signal = IsFirst
            ? new EventWaitHandle(false, EventResetMode.AutoReset, signalName)
            : null;
    }

    public static void SignalFirstInstance(string signalName = DefaultSignalName)
    {
        if (EventWaitHandle.TryOpenExisting(signalName, out EventWaitHandle? handle))
        {
            handle.Set();
            handle.Dispose();
        }
    }

    public bool WaitSignal(int millisecondsTimeout) =>
        _signal!.WaitOne(millisecondsTimeout);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _signal?.Dispose();
        try
        {
            if (IsFirst) _mutex.ReleaseMutex();
        }
        catch (ApplicationException) { }
        catch (ObjectDisposedException) { }
        _mutex.Dispose();
    }
}
