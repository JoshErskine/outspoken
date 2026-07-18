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
    public void LatencyCeiling_MatchesRevisedBudget()
    {
        // Revised from 2000 after T12 measured real hardware (ADR-002). Cleaned-dictation ceiling.
        Assert.Equal(3250, CoreInfo.LatencyCeilingMs);
    }
}
