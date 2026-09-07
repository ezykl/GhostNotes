using System;
using GhostNotes.Interop;

namespace GhostNotes.Services;

public sealed record HotkeySpec(uint Modifiers, uint VirtualKey, string Display);

public static class HotkeySelection
{
    public static HotkeySpec? Select(
        HotkeySpec preferred,
        HotkeySpec fallback,
        Func<HotkeySpec, bool> isAvailable)
    {
        if (isAvailable(preferred)) return preferred;
        if (isAvailable(fallback)) return fallback;
        return null;
    }
}
