namespace Outspoken.Core.Tests;

public class CoreInfoTests
{
    [Fact]
    public void WhisperThreadCap_NeverExceedsEight()
    {
        // ADR-002 hard constraint: oversubscribing the X Elite's cores collapses inference.
        Assert.InRange(CoreInfo.WhisperThreadCap, 1, 8);
    }

    [Fact]
    public void LatencyCeiling_IsTwoSeconds()
    {
        Assert.Equal(2000, CoreInfo.LatencyCeilingMs);
    }
}
