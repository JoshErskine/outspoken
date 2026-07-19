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
///
/// Dogfood finding (2026-07-14): a processor that lived through system sleep — especially
/// one that was mid-inference when the lid closed — comes back ~10x degraded (28–40s per
/// dictation vs ~3s in a fresh process). Two defenses: <see cref="Rebuild"/> for the app's
/// power-resume handler, and a watchdog that rebuilds the processor after any absurdly
/// slow transcription.
/// </summary>
public sealed class WhisperTranscriber : ITranscriber, IDisposable
{
    /// <summary>A healthy transcription is ~1.5–3.5s (30s encoder window, AC vs battery). Past this, the processor is presumed damaged.</summary>
    private static readonly TimeSpan WatchdogThreshold = TimeSpan.FromSeconds(10);

    private readonly string _modelPath;
    private readonly WhisperFactory _factory;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private WhisperProcessor _processor;
    private bool _rebuildRequested;
    // Whisper initial-prompt built from the user's custom vocabulary (T13). Reference assignment is
    // atomic; a rebuild reads it under _lock. Null/empty means no vocabulary priming.
    private volatile string? _vocabularyPrompt;

    private WhisperTranscriber(string modelPath, WhisperFactory factory, WhisperProcessor processor, TimeSpan modelLoadTime, string? vocabularyPrompt)
    {
        _modelPath = modelPath;
        _factory = factory;
        _processor = processor;
        ModelLoadTime = modelLoadTime;
        _vocabularyPrompt = vocabularyPrompt;
    }

    /// <summary>Cold-start cost, logged at startup — must stay off the dictation path.</summary>
    public TimeSpan ModelLoadTime { get; }

    /// <summary>Raised when the watchdog or a resume rebuild replaced the processor (operator-facing diagnostics).</summary>
    public event Action<string>? ProcessorRebuilt;

    /// <summary>Loads the model (downloading on first run) and warms the processor.</summary>
    /// <param name="vocabulary">Optional custom vocabulary (user setting, T13) to bias recognition.</param>
    public static async Task<WhisperTranscriber> CreateAsync(
        string? modelDirectory = null,
        IProgress<double>? downloadProgress = null,
        CancellationToken cancellationToken = default,
        string? vocabulary = null)
    {
        var modelPath = await WhisperModelStore.EnsureModelAsync(modelDirectory, downloadProgress, cancellationToken);

        var prompt = BuildVocabularyPrompt(vocabulary);
        var sw = Stopwatch.StartNew();
        var factory = WhisperFactory.FromPath(modelPath);
        var processor = BuildProcessor(factory, prompt);
        sw.Stop();

        return new WhisperTranscriber(modelPath, factory, processor, sw.Elapsed, prompt);
    }

    /// <summary>
    /// Requests a processor rebuild before the next transcription. Call on system resume:
    /// a processor that lived through sleep is not trusted.
    /// </summary>
    public void Rebuild() => _rebuildRequested = true;

    /// <summary>
    /// Swaps the custom vocabulary (T13). Takes effect on the next transcription via a processor
    /// rebuild - call after the user edits the vocabulary in settings.
    /// </summary>
    public void UpdateVocabulary(string? vocabulary)
    {
        var prompt = BuildVocabularyPrompt(vocabulary);
        if (prompt == _vocabularyPrompt)
            return; // no change, don't churn the processor
        _vocabularyPrompt = prompt;
        _rebuildRequested = true;
    }

    /// <summary>Turns a user vocabulary list into a Whisper initial prompt, or null when empty.</summary>
    private static string? BuildVocabularyPrompt(string? vocabulary)
    {
        var trimmed = vocabulary?.Trim();
        return string.IsNullOrEmpty(trimmed)
            ? null
            : $"This is a dictation that may reference names and terms such as: {trimmed}.";
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
            if (_rebuildRequested)
                ReplaceProcessor("system resume");

            var sw = Stopwatch.StartNew();
            var text = new System.Text.StringBuilder();
            await foreach (var segment in _processor.ProcessAsync(audio.Samples, cancellationToken))
                text.Append(segment.Text);
            sw.Stop();

            if (sw.Elapsed > WatchdogThreshold)
                ReplaceProcessor($"watchdog: transcription took {sw.Elapsed.TotalSeconds:F0}s");

            return text.ToString().Trim();
        }
        finally
        {
            _lock.Release();
        }
    }

    private void ReplaceProcessor(string reason)
    {
        _rebuildRequested = false;
        try
        {
            _processor.Dispose();
        }
        catch
        {
            // A damaged processor may fail its own dispose; the replacement matters more.
        }
        _processor = BuildProcessor(_factory, _vocabularyPrompt);
        ProcessorRebuilt?.Invoke(reason);
    }

    private static WhisperProcessor BuildProcessor(WhisperFactory factory, string? vocabularyPrompt)
    {
        // Decode tuning (T12): dictation is a single utterance, so take one segment with no
        // cross-segment context, and greedy with best-of 1 (a single decode pass) instead of the
        // default best-of resampling. Cuts decode compute with no measured accuracy loss.
        var builder = factory.CreateBuilder()
            .WithLanguage("en")
            .WithThreads(CoreInfo.WhisperThreadCap)
            .WithSingleSegment()
            .WithNoContext();

        // Vocabulary priming (T13): the user's custom vocabulary biases recognition toward their
        // product names / jargon. No terms configured -> no prompt (default behaviour).
        if (!string.IsNullOrEmpty(vocabularyPrompt))
            builder = builder.WithPrompt(vocabularyPrompt);

        var greedy = (GreedySamplingStrategyBuilder)builder.WithGreedySamplingStrategy();
        greedy.WithBestOf(1);
        return greedy.ParentBuilder.Build();
    }

    public void Dispose()
    {
        _processor.Dispose();
        _factory.Dispose();
        _lock.Dispose();
    }
}
