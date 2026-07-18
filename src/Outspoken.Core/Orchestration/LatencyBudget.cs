using System.Globalization;
using System.Text;

namespace Outspoken.Core.Orchestration;

/// <summary>One measured pipeline segment against its budget. <see cref="Over"/> is the pass/fail.</summary>
public readonly record struct BudgetLine(string Name, TimeSpan Measured, TimeSpan Limit)
{
    /// <summary>Over budget only when strictly past the limit - exactly on budget passes.</summary>
    public bool Over => Measured > Limit;

    /// <summary>Measured as a fraction of the limit (1.0 = on budget). 0 when there is no limit.</summary>
    public double Ratio => Limit > TimeSpan.Zero ? Measured.TotalMilliseconds / Limit.TotalMilliseconds : 0;
}

/// <summary>
/// The spec §3 per-segment latency budget and evaluation of a measured dictation against it.
/// Pure and testable - the source of truth for both the latency harness (T12, spec §8) and the
/// per-dictation budget line logged live in the tray app.
/// </summary>
public static class LatencyBudget
{
    // spec §3 budget table, key-release → text-at-cursor. Revised after T12 measured real hardware
    // (warm X Elite transcription ~1.5-2.0s; cleanup ~0.8-1.2s) - the original ≤1.0s/≤0.8s/≤0.2s
    // figures were optimistic. The EcoQoS throttling fix bounded the cold-start that dominated the
    // miss. See ADR-002 (T12); to be refined during the T14 dogfood week.
    public static readonly TimeSpan Transcribe = TimeSpan.FromMilliseconds(2000);
    public static readonly TimeSpan Cleanup = TimeSpan.FromMilliseconds(1000);
    public static readonly TimeSpan Inject = TimeSpan.FromMilliseconds(250);

    /// <summary>Full end-to-end ceiling for a cleaned dictation (<see cref="CoreInfo.LatencyCeilingMs"/>).</summary>
    public static readonly TimeSpan FullTotal = TimeSpan.FromMilliseconds(CoreInfo.LatencyCeilingMs);

    /// <summary>Raw-path target (no cleanup): transcription + injection, no LLM round-trip.</summary>
    public static readonly TimeSpan RawTotal = TimeSpan.FromMilliseconds(2250);

    /// <summary>Per-segment breakdown of a completed dictation against the budget, most-relevant lines first.</summary>
    public static IReadOnlyList<BudgetLine> Evaluate(DictationReport r)
    {
        var lines = new List<BudgetLine>
        {
            new("transcribe", r.TranscribeTime, Transcribe),
        };
        // Cleanup only applies when it actually ran; raw runs skip the line entirely.
        if (r.WasCleaned)
            lines.Add(new BudgetLine("cleanup", r.CleanupTime, Cleanup));
        lines.Add(new BudgetLine("inject", r.InjectTime, Inject));
        // Cleaned dictations answer to the 2.0s ceiling; raw dictations to the tighter 1.5s target.
        lines.Add(new BudgetLine(
            r.WasCleaned ? "total" : "total (raw)",
            r.TotalFromRelease,
            r.WasCleaned ? FullTotal : RawTotal));
        return lines;
    }

    /// <summary>A monospace budget table: name, measured, limit, and an ok/OVER verdict per line.</summary>
    public static string Format(IReadOnlyList<BudgetLine> lines)
    {
        var sb = new StringBuilder();
        foreach (var line in lines)
        {
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"  {line.Name,-14} {line.Measured.TotalSeconds,6:F2}s / {line.Limit.TotalSeconds,4:F2}s  {(line.Over ? "OVER" : "ok")}"));
        }
        return sb.ToString();
    }
}
