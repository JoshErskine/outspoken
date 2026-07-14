namespace Outspoken.Core.Audio;

/// <summary>
/// A finished dictation capture: mono float PCM at the Whisper input rate.
/// Lives only in memory — never written to disk or sent anywhere (ADR-001).
/// </summary>
public sealed record CapturedAudio(float[] Samples, int SampleRate)
{
    public const int WhisperSampleRate = 16_000;

    public TimeSpan Duration => TimeSpan.FromSeconds((double)Samples.Length / SampleRate);

    /// <summary>Loudest absolute sample (0..1). A near-zero peak means nothing was spoken.</summary>
    public float Peak
    {
        get
        {
            var peak = 0f;
            foreach (var s in Samples)
            {
                var a = Math.Abs(s);
                if (a > peak) peak = a;
            }
            return peak;
        }
    }
}
