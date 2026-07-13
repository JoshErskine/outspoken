namespace Outspoken.Core.Audio;

/// <summary>
/// Pure PCM conversion: interleaved float samples at any rate/channel count →
/// mono 16kHz float for Whisper. No NAudio types so it unit-tests without a device.
/// </summary>
public static class AudioConverter
{
    /// <summary>Averages interleaved channels down to mono. Returns the input untouched when already mono.</summary>
    public static float[] DownmixToMono(float[] interleaved, int channels)
    {
        if (channels <= 0)
            throw new ArgumentOutOfRangeException(nameof(channels));
        if (channels == 1)
            return interleaved;

        var frames = interleaved.Length / channels;
        var mono = new float[frames];
        for (var f = 0; f < frames; f++)
        {
            var sum = 0f;
            for (var c = 0; c < channels; c++)
                sum += interleaved[f * channels + c];
            mono[f] = sum / channels;
        }

        return mono;
    }

    /// <summary>
    /// Linear-interpolation resampler. Dictation speech through base.en is tolerant of the
    /// mild aliasing linear interpolation allows at 48k→16k; if transcription quality ever
    /// points here, swap in a windowed-sinc kernel behind this same signature.
    /// </summary>
    public static float[] Resample(float[] mono, int fromRate, int toRate)
    {
        if (fromRate <= 0 || toRate <= 0)
            throw new ArgumentOutOfRangeException(fromRate <= 0 ? nameof(fromRate) : nameof(toRate));
        if (fromRate == toRate || mono.Length == 0)
            return mono;

        var outLength = (int)((long)mono.Length * toRate / fromRate);
        var result = new float[outLength];
        var step = (double)fromRate / toRate;
        for (var i = 0; i < outLength; i++)
        {
            var pos = i * step;
            var i0 = (int)pos;
            var i1 = Math.Min(i0 + 1, mono.Length - 1);
            var frac = (float)(pos - i0);
            result[i] = mono[i0] + (mono[i1] - mono[i0]) * frac;
        }

        return result;
    }

    public static CapturedAudio ToWhisperFormat(float[] interleaved, int channels, int sampleRate)
    {
        var mono = DownmixToMono(interleaved, channels);
        var resampled = Resample(mono, sampleRate, CapturedAudio.WhisperSampleRate);
        return new CapturedAudio(resampled, CapturedAudio.WhisperSampleRate);
    }
}
