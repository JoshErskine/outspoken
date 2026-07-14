namespace Outspoken.Core.Cleanup;

/// <summary>Outcome of a cleanup attempt. <see cref="Text"/> is always safe to inject.</summary>
public sealed record CleanupResult(string Text, bool WasCleaned, string? FallbackReason = null)
{
    public static CleanupResult Cleaned(string text) => new(text, WasCleaned: true);
    public static CleanupResult Raw(string rawText, string reason) => new(rawText, WasCleaned: false, reason);
}

/// <summary>
/// LLM cleanup pass (spec §4). Implementations must honor the never-block rule:
/// timeout, offline, or API error returns the raw transcript, never throws
/// (ADR-001 §4 privacy + graceful-degradation invariant).
/// </summary>
public interface ICleanupClient
{
    Task<CleanupResult> CleanAsync(string rawTranscript, CancellationToken cancellationToken = default);
}
