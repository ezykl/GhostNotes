using System.Collections.Generic;
using GhostNotes.Services;
using Xunit;

public sealed class ClampTests
{
    private static readonly (double X, double Y, double W, double H)[] One =
        { (0, 0, 1920, 1080) };

    [Fact]
    public void IntersectingScreen_Unchanged()
    {
        var r = PositionClamp.Clamp(100, 100, 320, 220, One);
        Assert.Equal((100, 100, 320, 220), r);
    }

    [Fact]
    public void FullyOffScreenRight_MovedToPrimaryWithMargin()
    {
        var r = PositionClamp.Clamp(5000, 200, 320, 220, One);
        Assert.Equal((24, 24, 320, 220), r);
    }

    [Fact]
    public void NegativeCoordinates_MovedToPrimary()
    {
        var r = PositionClamp.Clamp(-500, -500, 320, 220, One);
        Assert.Equal((24, 24, 320, 220), r);
    }

    [Fact]
    public void NearestOfTwoScreens_Chosen()
    {
        var screens = new (double, double, double, double)[]
        {
            (0, 0, 1920, 1080),
            (1920, 0, 1920, 1080)
        };
        var r = PositionClamp.Clamp(4000, 200, 320, 220, screens);
        Assert.Equal(1944, r.X);
        Assert.Equal(24, r.Y);
    }

    [Fact]
    public void OversizedNote_ShrunkToScreenMinus48()
    {
        var r = PositionClamp.Clamp(5000, 200, 5000, 2000, One);
        Assert.Equal((24, 24, 1872, 1032), r);
    }

    [Theory]
    [InlineData(14, 2, 16)]
    [InlineData(48, 1, 48)]
    [InlineData(8, -1, 8)]
    [InlineData(10, -20, 8)]
    [InlineData(40, 20, 48)]
    public void FontZoom_ClampsBetween8And48(int current, int delta, int expected)
    {
        Assert.Equal(expected, FontZoom.Clamp(current, delta));
    }
}
