namespace Outspoken.Core.Transcription;

/// <summary>
/// Recognizes non-speech transcripts so they're never injected. Whisper emits sound
/// annotations for silence/noise (e.g. "[BLANK_AUDIO]", "[ Silence ]", "(music)") instead
/// of empty text; pasting those into the cursor is a bug (Josh, 2026-07-14).
/// </summary>
public static class TranscriptFilters
{
    public static bool IsBlank(string? transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript))
            return true;

        var t = transcript.Trim();

        // Whisper wraps every non-speech annotation in brackets or parentheses.
        if ((t.StartsWith('[') && t.EndsWith(']')) || (t.StartsWith('(') && t.EndsWith(')')))
            return true;

        // No letters or digits at all (pure punctuation like "." or "...") = nothing spoken.
        foreach (var c in t)
            if (char.IsLetterOrDigit(c))
                return false;

        return true;
    }
}
