using System;
using GhostNotes.Services;
using Xunit;

namespace GhostNotes.Tests;

public sealed class HotkeySelectionTests
{
    private static readonly HotkeySpec Preferred = new(2, 0x4E, "Ctrl+Alt+N");
    private static readonly HotkeySpec Fallback = new(6, 0x4E, "Ctrl+Alt+Shift+N");

    [Fact]
    public void PreferredAvailable_ReturnsPreferred()
    {
        Assert.Equal(Preferred, HotkeySelection.Select(Preferred, Fallback, _ => true));
    }

    [Fact]
    public void PreferredTaken_ReturnsFallback()
    {
        Assert.Equal(Fallback, HotkeySelection.Select(Preferred, Fallback, s => s.Equals(Fallback)));
    }

    [Fact]
    public void BothTaken_ReturnsNull()
    {
        Assert.Null(HotkeySelection.Select(Preferred, Fallback, _ => false));
    }
}
