using System;
using System.Threading;
using GhostNotes.Services;
using Xunit;

public sealed class AutosaveSchedulerTests
{
    [Fact]
    public void Trigger_FlushesAfterDelay()
    {
        using var done = new AutoResetEvent(false);
        using var scheduler = new AutosaveScheduler(() => done.Set(), TimeSpan.FromMilliseconds(50));
        scheduler.Trigger();
        Assert.True(done.WaitOne(2000));
    }

    [Fact]
    public void RepeatedTriggerWithinDelay_SingleFlush()
    {
        int count = 0;
        using var scheduler =
            new AutosaveScheduler(() => Interlocked.Increment(ref count), TimeSpan.FromMilliseconds(80));
        scheduler.Trigger();
        Thread.Sleep(20);
        scheduler.Trigger();
        Thread.Sleep(400);
        Assert.Equal(1, count);
    }

    [Fact]
    public void FlushNow_ImmediatelyFlushes_PendingNotRepeated()
    {
        int count = 0;
        using var scheduler =
            new AutosaveScheduler(() => Interlocked.Increment(ref count), TimeSpan.FromMilliseconds(80));
        scheduler.Trigger();
        scheduler.FlushNow();
        Assert.Equal(1, count);
        Thread.Sleep(400);
        Assert.Equal(1, count);
    }

    [Fact]
    public void Dispose_FlushesPending()
    {
        int count = 0;
        var scheduler =
            new AutosaveScheduler(() => Interlocked.Increment(ref count), TimeSpan.FromMilliseconds(80));
        scheduler.Trigger();
        scheduler.Dispose();
        Assert.Equal(1, count);
    }

    [Fact]
    public void Cancel_DiscardsPending()
    {
        int count = 0;
        var scheduler =
            new AutosaveScheduler(() => Interlocked.Increment(ref count), TimeSpan.FromMilliseconds(50));
        scheduler.Trigger();
        scheduler.Cancel();
        scheduler.Dispose();
        Thread.Sleep(300);
        Assert.Equal(0, count);
    }
}
