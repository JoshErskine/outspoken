using System.IO;
using System.Media;

namespace Outspoken.Core.Audio;

/// <summary>
/// Plays the overlay's start/stop cues from bundled WAV assets via <see cref="SoundPlayer"/>
/// (the OS PlaySound API — works from any thread, no device/format quirks). The cues are
/// royalty-free recorded sounds chosen by Josh (T10), passed in as raw WAV bytes so this stays
/// decoupled from where the assets live. Toggleable (on by default; settings toggle at T11).
/// </summary>
public sealed class AudioCuePlayer : IDisposable
{
    private readonly SoundPlayer _start;
    private readonly SoundPlayer _stop;

    /// <param name="startWav">Raw WAV file bytes for the press cue.</param>
    /// <param name="stopWav">Raw WAV file bytes for the release/processing cue.</param>
    public AudioCuePlayer(byte[] startWav, byte[] stopWav)
    {
        _start = Load(startWav);
        _stop = Load(stopWav);
    }

    /// <summary>When false, cues are silent (the audio-cues setting).</summary>
    public bool Enabled { get; set; } = true;

    public void PlayStart() => Play(_start);
    public void PlayStop() => Play(_stop);

    private void Play(SoundPlayer player)
    {
        if (!Enabled)
            return;
        try
        {
            player.Play(); // async, background thread — a cue must never block or break dictation
        }
        catch
        {
            // A missing/blocked output device is not a dictation failure.
        }
    }

    public void Dispose()
    {
        _start.Dispose();
        _stop.Dispose();
    }

    private static SoundPlayer Load(byte[] wav)
    {
        var player = new SoundPlayer(new MemoryStream(wav));
        player.Load();
        return player;
    }
}
