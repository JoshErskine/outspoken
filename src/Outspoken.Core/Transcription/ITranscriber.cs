using Outspoken.Core.Audio;

namespace Outspoken.Core.Transcription;

/// <summary>
/// Speech-to-text over a finished capture. Implementations keep their model warm across
/// dictations — model load must never sit on the key-release → text path (ADR-002: the
/// 10s real-voice case already costs ~1.65s; there is no budget for a per-call load).
/// </summary>
public interface ITranscriber
{
    /// <summary>Transcribes mono 16kHz audio to raw text (no cleanup).</summary>
    Task<string> TranscribeAsync(CapturedAudio audio, CancellationToken cancellationToken = default);
}
