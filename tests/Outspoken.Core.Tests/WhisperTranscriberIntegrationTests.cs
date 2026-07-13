using NAudio.Wave;
using Outspoken.Core.Audio;
using Outspoken.Core.Transcription;

namespace Outspoken.Core.Tests;

/// <summary>
/// T5 verify: real fixture WAV → WhisperTranscriber → fuzzy match ≥90% against the
/// known transcript. Needs the T1 benchmark's local model + recorded fixtures (both
/// gitignored — the clips are Josh's voice), so it skips cleanly anywhere else (CI).
/// </summary>
public class WhisperTranscriberIntegrationTests
{
    private static readonly string RepoRoot =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    private static readonly string BenchmarkDir = Path.Combine(RepoRoot, "tools", "WhisperBenchmark");
    private static readonly string ModelsDir = Path.Combine(BenchmarkDir, "models");
    private static readonly string FixturesDir = Path.Combine(BenchmarkDir, "fixtures");

    public static bool FixturesAvailable =>
        File.Exists(Path.Combine(ModelsDir, WhisperModelStore.ModelFileName)) &&
        File.Exists(Path.Combine(FixturesDir, "clip_10s.wav")) &&
        File.Exists(Path.Combine(FixturesDir, "clip_10s.expected.txt"));

    [SkippableFact]
    public async Task TenSecondFixture_TranscribesAtNinetyPercentOrBetter()
    {
        Skip.IfNot(FixturesAvailable, "Local Whisper model/fixtures not present (gitignored) — run on the dev machine.");

        using var transcriber = await WhisperTranscriber.CreateAsync(ModelsDir);
        var audio = LoadWav(Path.Combine(FixturesDir, "clip_10s.wav"));
        var expected = await File.ReadAllTextAsync(Path.Combine(FixturesDir, "clip_10s.expected.txt"));

        var start = System.Diagnostics.Stopwatch.StartNew();
        var actual = await transcriber.TranscribeAsync(audio);
        start.Stop();

        var similarity = TranscriptSimilarity.Compare(expected, actual);
        Assert.True(similarity >= 0.90,
            $"Similarity {similarity:P1} below 90% gate. Got: \"{actual}\"");

        // Evidence for the build log; ADR-002 budget context.
        Console.WriteLine($"cold-start (load+warm): {transcriber.ModelLoadTime.TotalMilliseconds:F0}ms, " +
                          $"transcribe: {start.Elapsed.TotalSeconds:F2}s, similarity: {similarity:P1}");
    }

    private static CapturedAudio LoadWav(string path)
    {
        using var reader = new WaveFileReader(path);
        var sampleProvider = reader.ToSampleProvider();
        var samples = new List<float>((int)(reader.SampleCount * reader.WaveFormat.Channels));
        var buffer = new float[8192];
        int read;
        while ((read = sampleProvider.Read(buffer, 0, buffer.Length)) > 0)
            samples.AddRange(buffer.Take(read));

        return AudioConverter.ToWhisperFormat([.. samples], reader.WaveFormat.Channels, reader.WaveFormat.SampleRate);
    }
}
