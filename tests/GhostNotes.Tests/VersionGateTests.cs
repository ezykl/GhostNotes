using GhostNotes.Interop;
using Xunit;

public sealed class VersionGateTests
{
    [Theory]
    [InlineData(19041, true)]
    [InlineData(19042, true)]
    [InlineData(26200, true)]
    [InlineData(19040, false)]
    [InlineData(7601, false)]
    public void ExcludeFromCapture_SupportMatchesBuildFloor(int build, bool expected)
    {
        Assert.Equal(expected, new VersionGate(build).SupportsExcludeFromCapture);
    }
}
