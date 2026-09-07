using System;
using System.Collections.Generic;
using GhostNotes.Interop;
using Xunit;

public sealed class CaptureGuardTests
{
    private const uint Pid = 42;

    private sealed class FakeApi : IAffinityApi
    {
        public Dictionary<IntPtr, uint> State { get; } = new();
        public HashSet<IntPtr> FailFor { get; } = new();

        public bool SetAffinity(IntPtr hwnd, uint affinity)
        {
            if (FailFor.Contains(hwnd)) return false;
            State[hwnd] = affinity;
            return true;
        }

        public bool TryGetAffinity(IntPtr hwnd, out uint affinity) =>
            State.TryGetValue(hwnd, out affinity);
    }

    private sealed class FakeEnum : IWindowEnumerator
    {
        public List<(IntPtr Hwnd, uint ProcessId, bool Visible, uint Affinity)> Windows { get; } = new();

        public IEnumerable<(IntPtr Hwnd, uint ProcessId, bool Visible, uint Affinity)> Enumerate() =>
            Windows;
    }

    private static readonly IntPtr H1 = new(1);

    [Fact]
    public void Sweep_OurVisibleWindow_GetsExcludeFromCapture()
    {
        var api = new FakeApi();
        var windows = new FakeEnum { Windows = { (H1, Pid, true, NativeMethods.WDA_NONE) } };
        var guard = new CaptureGuard(api, windows, new VersionGate(26200), Pid);

        guard.Sweep();

        Assert.Equal(NativeMethods.WDA_EXCLUDEFROMCAPTURE, api.State[H1]);
        Assert.True(guard.IsProtected);
    }

    [Fact]
    public void Sweep_ForeignWindow_Untouched()
    {
        var api = new FakeApi();
        var windows = new FakeEnum { Windows = { (H1, 77, true, NativeMethods.WDA_NONE) } };
        var guard = new CaptureGuard(api, windows, new VersionGate(26200), Pid);

        guard.Sweep();

        Assert.False(api.State.ContainsKey(H1));
        Assert.True(guard.IsProtected);
    }

    [Fact]
    public void ApplyFailure_ReportsUnprotected_AndRaisesEvent()
    {
        var api = new FakeApi { FailFor = { H1 } };
        var windows = new FakeEnum();
        var guard = new CaptureGuard(api, windows, new VersionGate(26200), Pid);
        bool? lastEvent = null;
        guard.ProtectionStatusChanged += (_, on) => lastEvent = on;

        guard.ApplyToWindow(H1);

        Assert.False(guard.IsProtected);
        Assert.False(lastEvent);
    }

    [Fact]
    public void OldBuild_UsesMonitorFallback()
    {
        var api = new FakeApi();
        var guard = new CaptureGuard(api, new FakeEnum(), new VersionGate(19040), Pid);

        guard.ApplyToWindow(H1);

        Assert.Equal(NativeMethods.WDA_MONITOR, api.State[H1]);
    }

    [Fact]
    public void ApplyToWindow_AppliesImmediately()
    {
        var api = new FakeApi();
        var guard = new CaptureGuard(api, new FakeEnum(), new VersionGate(26200), Pid);

        guard.ApplyToWindow(H1);

        Assert.Equal(NativeMethods.WDA_EXCLUDEFROMCAPTURE, api.State[H1]);
    }

    [Fact]
    public void Sweep_ReassertsResetExplicitWindow()
    {
        var api = new FakeApi();
        var guard = new CaptureGuard(api, new FakeEnum(), new VersionGate(26200), Pid);
        guard.ApplyToWindow(H1);

        api.State[H1] = NativeMethods.WDA_NONE;
        guard.Sweep();

        Assert.Equal(NativeMethods.WDA_EXCLUDEFROMCAPTURE, api.State[H1]);
        Assert.True(guard.IsProtected);
    }

    [Fact]
    public void Forget_RemovesWindowFromWatch()
    {
        var api = new FakeApi();
        var guard = new CaptureGuard(api, new FakeEnum(), new VersionGate(26200), Pid);
        guard.ApplyToWindow(H1);
        api.State[H1] = NativeMethods.WDA_NONE;

        guard.Forget(H1);
        guard.Sweep();

        Assert.Equal(NativeMethods.WDA_NONE, api.State[H1]);
    }
}
