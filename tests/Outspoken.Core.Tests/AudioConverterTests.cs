using Outspoken.Core.Audio;

namespace Outspoken.Core.Tests;

public class AudioConverterTests
{
    [Fact]
    public void DownmixStereo_AveragesChannels()
    {
        float[] interleaved = [1f, 0f, 0.5f, 0.5f, -1f, 1f];
        var mono = AudioConverter.DownmixToMono(interleaved, channels: 2);
        Assert.Equal([0.5f, 0.5f, 0f], mono);
    }

    [Fact]
    public void DownmixMono_ReturnsInputUnchanged()
    {
        float[] samples = [0.1f, 0.2f];
        Assert.Same(samples, AudioConverter.DownmixToMono(samples, channels: 1));
    }

    [Fact]
    public void Resample_48kTo16k_ThirdsTheLength()
    {
        var input = new float[48_000]; // 1 second at 48kHz
        var output = AudioConverter.Resample(input, 48_000, 16_000);
        Assert.Equal(16_000, output.Length);
    }

    [Fact]
    public void Resample_SameRate_ReturnsInputUnchanged()
    {
        float[] samples = [0.1f, 0.2f];
        Assert.Same(samples, AudioConverter.Resample(samples, 16_000, 16_000));
    }

    [Fact]
    public void Resample_PreservesSineShape()
    {
        // 440Hz sine at 48kHz should still be a 440Hz sine at 16kHz.
        const int fromRate = 48_000, toRate = 16_000;
        var input = new float[fromRate];
        for (var i = 0; i < input.Length; i++)
            input[i] = MathF.Sin(2 * MathF.PI * 440 * i / fromRate);

        var output = AudioConverter.Resample(input, fromRate, toRate);

        for (var i = 0; i < output.Length; i++)
        {
            var expected = MathF.Sin(2 * MathF.PI * 440 * i / toRate);
            Assert.True(MathF.Abs(output[i] - expected) < 0.02f, $"sample {i}: {output[i]} vs {expected}");
        }
    }

    [Fact]
    public void ToWhisperFormat_StereoFortyEightK_YieldsMonoSixteenK()
    {
        var interleaved = new float[48_000 * 2]; // 1s stereo 48k
        var result = AudioConverter.ToWhisperFormat(interleaved, channels: 2, sampleRate: 48_000);

        Assert.Equal(CapturedAudio.WhisperSampleRate, result.SampleRate);
        Assert.Equal(16_000, result.Samples.Length);
        Assert.Equal(TimeSpan.FromSeconds(1), result.Duration);
    }
}
