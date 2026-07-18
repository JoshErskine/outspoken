using Outspoken.Core.Injection;
using Outspoken.Core.Orchestration;

namespace Outspoken.Core.Tests;

public class LatencyBudgetTests
{
    [Fact]
    public void Limits_MatchRevisedBudget()
    {
        // Budget revised after T12 measured real hardware (ADR-002), key-release → text-at-cursor.
        Assert.Equal(2000, LatencyBudget.Transcribe.TotalMilliseconds);
        Assert.Equal(1000, LatencyBudget.Cleanup.TotalMilliseconds);
        Assert.Equal(250, LatencyBudget.Inject.TotalMilliseconds);
        Assert.Equal(CoreInfo.LatencyCeilingMs, LatencyBudget.FullTotal.TotalMilliseconds);
        Assert.Equal(2250, LatencyBudget.RawTotal.TotalMilliseconds);
    }

    [Fact]
    public void Line_Over_IsTrueOnlyWhenMeasuredExceedsLimit()
    {
        var under = new BudgetLine("x", TimeSpan.FromMilliseconds(1999), LatencyBudget.Transcribe);
        var exact = new BudgetLine("x", TimeSpan.FromMilliseconds(2000), LatencyBudget.Transcribe);
        var over = new BudgetLine("x", TimeSpan.FromMilliseconds(2001), LatencyBudget.Transcribe);

        Assert.False(under.Over);
        Assert.False(exact.Over); // exactly on budget passes
        Assert.True(over.Over);
    }

    [Fact]
    public void Evaluate_CleanedDictation_UsesFullCeilingForTotal()
    {
        var report = MakeReport(transcribe: 900, cleanup: 600, inject: 100, total: 1600, wasCleaned: true, raw: false);

        var lines = LatencyBudget.Evaluate(report);

        var total = lines.Single(l => l.Name.StartsWith("total"));
        Assert.Equal(LatencyBudget.FullTotal, total.Limit);
        Assert.False(total.Over); // 1.6s < 2.0s ceiling
        Assert.All(lines, l => Assert.False(l.Over));
    }

    [Fact]
    public void Evaluate_RawDictation_UsesRawTargetForTotal()
    {
        // Raw path (no cleanup): the 2.25s raw target applies; a slow throttled run blows it.
        var report = MakeReport(transcribe: 2400, cleanup: 0, inject: 90, total: 2600, wasCleaned: false, raw: true);

        var lines = LatencyBudget.Evaluate(report);

        var total = lines.Single(l => l.Name.StartsWith("total"));
        Assert.Equal(LatencyBudget.RawTotal, total.Limit);
        Assert.True(total.Over); // 2.6s > 2.25s raw target
        Assert.True(lines.Single(l => l.Name == "transcribe").Over); // 2.4s > 2.0s
        Assert.DoesNotContain(lines, l => l.Name == "cleanup"); // raw skips the cleanup line
    }

    [Fact]
    public void Format_MarksOverBudgetLines()
    {
        var lines = new[]
        {
            new BudgetLine("transcribe", TimeSpan.FromMilliseconds(2400), LatencyBudget.Transcribe),
            new BudgetLine("inject", TimeSpan.FromMilliseconds(90), LatencyBudget.Inject),
        };

        var table = LatencyBudget.Format(lines);

        Assert.Contains("transcribe", table);
        Assert.Contains("2.40", table); // measured seconds rendered
        // The over-budget line is flagged and the in-budget line is not the same marker.
        Assert.Contains("OVER", table);
        Assert.Contains("ok", table);
    }

    private static DictationReport MakeReport(
        double transcribe, double cleanup, double inject, double total, bool wasCleaned, bool raw) =>
        new(
            Text: "hello world",
            Outcome: InjectionOutcome.Injected,
            RawMode: raw,
            WasCleaned: wasCleaned,
            AudioDuration: TimeSpan.FromSeconds(10),
            TranscribeTime: TimeSpan.FromMilliseconds(transcribe),
            CleanupTime: TimeSpan.FromMilliseconds(cleanup),
            InjectTime: TimeSpan.FromMilliseconds(inject),
            TotalFromRelease: TimeSpan.FromMilliseconds(total));
}
