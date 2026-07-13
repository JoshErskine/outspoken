namespace Outspoken.Core;

/// <summary>Pipeline-wide constants. Values trace to the ADRs; changing them is a decision, not a tweak.</summary>
public static class CoreInfo
{
    /// <summary>Whisper worker thread cap (ADR-002: 12 threads collapses to 40x realtime on the X Elite; never use Environment.ProcessorCount).</summary>
    public const int WhisperThreadCap = 8;

    /// <summary>End-to-end latency ceiling, key-release to text-at-cursor, in milliseconds (spec §8).</summary>
    public const int LatencyCeilingMs = 2000;
}
