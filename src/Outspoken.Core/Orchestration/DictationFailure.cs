namespace Outspoken.Core.Orchestration;

/// <summary>
/// Why a dictation could not complete (error matrix, T13). The kind selects the short
/// overlay message; the detail is operator-facing (error log only) and never the pill text,
/// never dictation content.
/// </summary>
public enum DictationFailureKind
{
    /// <summary>Capture start or stop failed — no audio was recorded.</summary>
    Microphone,

    /// <summary>Transcription threw or stalled past the watchdog — no transcript was produced.</summary>
    Transcription,
}

/// <param name="Kind">Selects the overlay message.</param>
/// <param name="Detail">Operator-facing detail for the error log — never shown on the pill, never dictation content.</param>
public sealed record DictationFailure(DictationFailureKind Kind, string Detail);
