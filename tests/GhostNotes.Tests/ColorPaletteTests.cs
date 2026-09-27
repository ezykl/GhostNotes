using System;
using System.IO;
using GhostNotes;
using GhostNotes.Interop;
using GhostNotes.Services;
using Xunit;

namespace GhostNotes.Tests;

public sealed class ColorPaletteTests
{
    [Fact]
    public void ColorPalette_StartsNonYellow_AndEndsWithYellow()
    {
        Assert.NotEqual("#FFF59D", NoteManager.ColorPalette[0]);
        Assert.Equal("#FFF59D", NoteManager.ColorPalette[^1]);
    }

    [Fact]
    public void GetNextTint_CyclesThroughAllColorsSequentially()
    {
        var dir = Path.Combine(Path.GetTempPath(), "test_pal_" + Guid.NewGuid().ToString("N"));
        var repo = new NoteRepository(dir);
        var guard = new CaptureGuard(
            new NativeAffinityApi(),
            new ProcessWindowEnumerator(),
            new VersionGate(26200),
            123);
        var manager = new NoteManager(repo, guard);

        var first = manager.GetNextTint();
        Assert.Equal("#BBDEFB", first);

        var second = manager.GetNextTint();
        Assert.Equal("#C8E6C9", second);

        var third = manager.GetNextTint();
        Assert.Equal("#F8BBD0", third);

        var fourth = manager.GetNextTint();
        Assert.Equal("#D1C4E9", fourth);

        var fifth = manager.GetNextTint();
        Assert.Equal("#FFFFFF", fifth);

        var sixth = manager.GetNextTint();
        Assert.Equal("#FFF59D", sixth);

        // Cycle wraps around
        var seventh = manager.GetNextTint();
        Assert.Equal("#BBDEFB", seventh);
    }

    [Fact]
    public void GetNextTint_BasedOnYellow_ReturnsAlternateSkyBlue()
    {
        var dir = Path.Combine(Path.GetTempPath(), "test_pal_" + Guid.NewGuid().ToString("N"));
        var repo = new NoteRepository(dir);
        var guard = new CaptureGuard(
            new NativeAffinityApi(),
            new ProcessWindowEnumerator(),
            new VersionGate(26200),
            123);
        var manager = new NoteManager(repo, guard);

        // When based on Yellow, next should alternate to Sky Blue
        var nextFromYellow = manager.GetNextTint("#FFF59D");
        Assert.Equal("#BBDEFB", nextFromYellow);

        // Next from Sky Blue should be Mint Green
        var nextFromSky = manager.GetNextTint("#BBDEFB");
        Assert.Equal("#C8E6C9", nextFromSky);
    }
}
