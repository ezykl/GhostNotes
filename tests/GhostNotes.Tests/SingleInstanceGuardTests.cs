using System;
using System.Threading;
using GhostNotes.Services;
using Xunit;

namespace GhostNotes.Tests;

public sealed class SingleInstanceGuardTests
{
    [Fact]
    public void FirstAcquires_SecondDenied_ReleasedThenReacquired()
    {
        string name = "GhostNotes_T_" + Guid.NewGuid().ToString("N");
        using var first = new SingleInstanceGuard(name, name + "_sig");
        Assert.True(first.IsFirst);

        using (var second = new SingleInstanceGuard(name, name + "_sig"))
            Assert.False(second.IsFirst);

        first.Dispose();
        using var third = new SingleInstanceGuard(name, name + "_sig");
        Assert.True(third.IsFirst);
    }

    [Fact]
    public void Signal_WakesFirstInstance()
    {
        string name = "GhostNotes_T_" + Guid.NewGuid().ToString("N");
        using var first = new SingleInstanceGuard(name, name + "_sig");
        SingleInstanceGuard.SignalFirstInstance(name + "_sig");
        Assert.True(first.WaitSignal(2000));
    }
}
