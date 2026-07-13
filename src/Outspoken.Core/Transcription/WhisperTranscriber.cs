using System.Diagnostics;
using Outspoken.Core.Audio;
using Whisper.net;

namespace Outspoken.Core.Transcription;

/// <summary>
/// whisper.cpp via Whisper.net (ADR-002: base.en-q5_1, thread cap
/// <see cref="CoreInfo.WhisperThreadCap"/> — 12 threads collapses 40x on the X Elite).
/// The factory and processor are built once and reused, so the model is warm and
/// key-release only pays inference. Transcriptions are serialized by a semaphore:
/// a WhisperProcessor is not safe for concurrent use, and dictations are sequential anyway.
/// </summary>
public sealed class WhisperTranscriber : ITranscriber, IDisposable
{
    private readonly WhisperFactory _factory;
    private readonly WhisperProcessor _processor;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private WhisperTranscriber(WhisperFactory factory, WhisperProcessor processor, TimeSpan modelLoadTime)
    {
        _factory = factory;
        _processor = processor;
        ModelLoadTime = modelLoadTime;
    }

    /// <summary>Cold-start cost, logged at startup — must stay off the dictation path.</summary>
    public TimeSpan ModelLoadTime { get; }

    /// <summary>Loads the model (downloading on first run) and warms the processor.</summary>
    public static async Task<WhisperTranscriber> CreateAsync(
        string? modelDirectory = null,
        IProgress<double>? downloadProgress = null,
        CancellationToken cancellationToken = default)
    {
        var modelPath = await WhisperModelStore.EnsureModelAsync(modelDirectory, downloadProgress, cancellationToken);

        var sw = Stopwatch.StartNew();
        var factory = WhisperFactory.FromPath(modelPath);
        var processor = factory.CreateBuilder()
            .WithLanguage("en")
            .WithThreads(CoreInfo.WhisperThreadCap)
            .Build();
        sw.Stop();

        return new WhisperTranscriber(factory, processor, sw.Elapsed);
    }

    public async Task<string> TranscribeAsync(CapturedAudio audio, CancellationToken cancellationToken = default)
    {
        if (audio.SampleRate != CapturedAudio.WhisperSampleRate)
            throw new ArgumentException($"Expected {CapturedAudio.WhisperSampleRate}Hz audio, got {audio.SampleRate}Hz.", nameof(audio));
        if (audio.Samples.Length == 0)
            return string.Empty;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            var text = new System.Text.StringBuilder();
            await foreach (var segment in _processor.ProcessAsync(audio.Samples, cancellationToken))
                text.Append(segment.Text);
            return text.ToString().Trim();
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose()
    {
        _processor.Dispose();
        _factory.Dispose();
        _lock.Dispose();
    }
}
