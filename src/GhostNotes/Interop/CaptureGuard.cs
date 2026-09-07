using System;
using System.Collections.Generic;

namespace GhostNotes.Interop;

public interface IAffinityApi
{
    bool SetAffinity(IntPtr hwnd, uint affinity);
    bool TryGetAffinity(IntPtr hwnd, out uint affinity);
}

public interface IWindowEnumerator
{
    IEnumerable<(IntPtr Hwnd, uint ProcessId, bool Visible, uint Affinity)> Enumerate();
}

public sealed class NativeAffinityApi : IAffinityApi
{
    public bool SetAffinity(IntPtr hwnd, uint affinity) =>
        NativeMethods.SetWindowDisplayAffinity(hwnd, affinity);

    public bool TryGetAffinity(IntPtr hwnd, out uint affinity) =>
        NativeMethods.GetWindowDisplayAffinity(hwnd, out affinity);
}

public sealed class ProcessWindowEnumerator : IWindowEnumerator
{
    public IEnumerable<(IntPtr Hwnd, uint ProcessId, bool Visible, uint Affinity)> Enumerate()
    {
        var result = new List<(IntPtr, uint, bool, uint)>();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            bool visible = NativeMethods.IsWindowVisible(hwnd);
            NativeMethods.GetWindowDisplayAffinity(hwnd, out uint affinity);
            result.Add((hwnd, pid, visible, affinity));
            return true;
        }, IntPtr.Zero);
        return result;
    }
}

public sealed class CaptureGuard
{
    private readonly IAffinityApi _api;
    private readonly IWindowEnumerator _enumerator;
    private readonly uint _affinityValue;
    private readonly uint _processId;
    private readonly List<IntPtr> _explicit = new();

    public event EventHandler<bool>? ProtectionStatusChanged;

    public bool IsProtected { get; private set; } = true;

    public CaptureGuard(IAffinityApi api, IWindowEnumerator enumerator, VersionGate gate, uint processId)
    {
        _api = api;
        _enumerator = enumerator;
        _processId = processId;
        _affinityValue = gate.SupportsExcludeFromCapture
            ? NativeMethods.WDA_EXCLUDEFROMCAPTURE
            : NativeMethods.WDA_MONITOR;
    }

    public void ApplyToWindow(IntPtr hwnd)
    {
        lock (_explicit)
        {
            if (!_explicit.Contains(hwnd)) _explicit.Add(hwnd);
        }
        SetStatus(_api.SetAffinity(hwnd, _affinityValue));
    }

    public void Forget(IntPtr hwnd)
    {
        lock (_explicit) { _explicit.Remove(hwnd); }
    }

    public void Sweep()
    {
        bool allOk = true;
        foreach (var (hwnd, pid, visible, affinity) in _enumerator.Enumerate())
        {
            if (pid != _processId || !visible) continue;
            if (affinity != _affinityValue && !_api.SetAffinity(hwnd, _affinityValue))
                allOk = false;
        }
        lock (_explicit)
        {
            foreach (var hwnd in _explicit)
            {
                if (!_api.TryGetAffinity(hwnd, out uint a) || a != _affinityValue)
                {
                    if (!_api.SetAffinity(hwnd, _affinityValue)) allOk = false;
                }
            }
        }
        SetStatus(allOk);
    }

    private void SetStatus(bool ok)
    {
        if (IsProtected == ok) return;
        IsProtected = ok;
        ProtectionStatusChanged?.Invoke(this, ok);
    }
}
