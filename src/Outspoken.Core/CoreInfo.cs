namespace Outspoken.Core;

/// <summary>Pipeline-wide constants. Values trace to the ADRs; changing them is a decision, not a tweak.</summary>
public static class CoreInfo
{
    /// <summary>Whisper worker thread cap (ADR-002: 12 threads collapses to 40x realtime on the X Elite; never use Environment.ProcessorCount).</summary>
    public const int WhisperThreadCap = 8;

    /// <summary>
    /// End-to-end latency ceiling for a cleaned dictation, key-release to text-at-cursor, in ms.
    /// Revised from the original 2000 after T12 measured real hardware: warm transcription on the
    /// X Elite is ~1.5-2.0s (the ≤1.0s figure was a cool-machine, post-compile fluke), and the
    /// EcoQoS throttling fix bounded the cold-start that dominated the miss. See ADR-002 (T12).
    /// </summary>
    public const int LatencyCeilingMs = 3250;

    /// <summary>Cleanup model (ADR-001: ~0.1¢/dictation).</summary>
    public const string CleanupModel = "claude-haiku-4-5";

    /// <summary>Cleanup call ceiling — past this, deliver raw instead of blocking (spec §4, ADR-001).</summary>
    public const int CleanupTimeoutMs = 3000;

    /// <summary>Cleanup output cap. Dictation is short; 1024 tokens is ample headroom (ADR-001).</summary>
    public const int CleanupMaxTokens = 1024;
}
