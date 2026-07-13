namespace Outspoken.Core.Audio;

/// <summary>
/// Push-to-talk microphone capture. Start on hotkey-down, Stop on release.
/// Implementations must hold audio in memory only and release the microphone
/// between captures (spec acceptance #5 — mic indicator must clear).
/// </summary>
public interface IAudioCaptureService
{
    /// <summary>Opens the default capture device and starts buffering.</summary>
    void Start();

    /// <summary>
    /// Stops capturing, releases the device, and returns the buffered audio
    /// converted to mono 16kHz float PCM.
    /// </summary>
    CapturedAudio Stop();

    /// <summary>Latest input level (RMS, 0..1-ish) while capturing — drives the overlay waveform (T9).</summary>
    float CurrentLevel { get; }
}
