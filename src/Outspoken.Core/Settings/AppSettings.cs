namespace Outspoken.Core.Settings;

/// <summary>
/// User-tunable settings, persisted as JSON (see <see cref="SettingsStore"/>). The API key is
/// NOT here — it stays DPAPI-encrypted in its own store (ADR-001). Defaults are the V1 shipping
/// behaviour, so a fresh install with no settings file behaves correctly.
/// </summary>
public sealed record AppSettings
{
    /// <summary>Soft start/stop cue sounds (Design Direction §Audio cues — on by default).</summary>
    public bool AudioCuesEnabled { get; init; } = true;

    /// <summary>When true, dictation skips LLM cleanup by default (Shift still toggles per-dictation).</summary>
    public bool RawModeDefault { get; init; }

    /// <summary>Start Outspoken automatically when Windows starts (HKCU Run key).</summary>
    public bool LaunchAtLogin { get; init; }

    /// <summary>
    /// Custom vocabulary (T13): comma- or line-separated names and terms the user dictates (product
    /// names, jargon) that base.en tends to mishear. Primes the Whisper decoder toward these
    /// spellings. Stored locally, edited in Settings - deliberately not shipped in the repo.
    /// </summary>
    public string CustomVocabulary { get; init; } = "";
}
