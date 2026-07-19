using System.Diagnostics;
using Outspoken.Core.Audio;
using Outspoken.Core.Cleanup;
using Outspoken.Core.Hotkeys;
using Outspoken.Core.Injection;
using Outspoken.Core.Transcription;

namespace Outspoken.Core.Orchestration;

public enum DictationState
{
    Idle,
    Listening,
    Processing,
}

/// <summary>What a completed dictation looked like — the timings feed the latency harness (T12, spec §8).</summary>
public sealed record DictationReport(
    string Text,
    InjectionOutcome Outcome,
    bool RawMode,
    bool WasCleaned,
    TimeSpan AudioDuration,
    TimeSpan TranscribeTime,
    TimeSpan CleanupTime,
    TimeSpan InjectTime,
    TimeSpan TotalFromRelease,
    string? CleanupFallbackReason = null);

/// <summary>
/// The walking skeleton (T7): wires hotkey → capture → transcribe → inject into the
/// dictation loop. Hold events arrive on the hook thread; processing runs on the thread
/// pool; consumers marshal UI updates themselves.
///
/// A new hold may start while the previous dictation is still transcribing (the
/// transcriber serializes internally) — capture is independent, so nothing is dropped.
/// </summary>
public sealed class DictationOrchestrator : IDisposable
{
    private readonly IHotkeySource _hotkeys;
    private readonly IAudioCaptureService _audio;
    private readonly ITranscriber _transcriber;
    private readonly IInjector _injector;
    private ICleanupClient? _cleanup;

    /// <param name="cleanup">Optional. Null (no key configured, or Shift-held raw runs) delivers raw transcripts.</param>
    public DictationOrchestrator(IHotkeySource hotkeys, IAudioCaptureService audio, ITranscriber transcriber, IInjector injector, ICleanupClient? cleanup = null)
    {
        _hotkeys = hotkeys;
        _audio = audio;
        _transcriber = transcriber;
        _injector = injector;
        _cleanup = cleanup;
        _hotkeys.HoldStarted += OnHoldStarted;
        _hotkeys.HoldEnded += OnHoldEnded;
    }

    public DictationState State { get; private set; } = DictationState.Idle;

    public event Action<DictationState>? StateChanged;
    public event Action<DictationReport>? Completed;

    /// <summary>A stage failed with no text to deliver. The kind selects the overlay message; the detail is operator-facing.</summary>
    public event Action<DictationFailure>? Failed;

    /// <summary>
    /// A transcription that runs past this is treated as hung (not merely slow) and cancelled →
    /// <see cref="DictationFailureKind.Transcription"/>, so the pill can never freeze in Processing
    /// (the residual hazard from the 2026-07-19 battery investigation). Well above the worst
    /// legitimate cold transcribe (~7.5s pre-T12-fix); the transcriber's own 10s watchdog handles
    /// the slow-but-completes case, so this only ever fires on a true stall.
    /// </summary>
    public TimeSpan TranscriptionStallTimeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Nothing was spoken (silent hold, quick tap, or Whisper blank annotation). Dismiss quietly — no paste, no error.</summary>
    public event Action? NoSpeech;

    /// <summary>Below this peak, the capture is treated as silence and skips transcription entirely (instant dismiss).</summary>
    public const float SilencePeak = 0.01f;

    private void OnHoldStarted()
    {
        try
        {
            _audio.Start();
            SetState(DictationState.Listening);
        }
        catch (Exception ex)
        {
            Failed?.Invoke(new DictationFailure(DictationFailureKind.Microphone, $"mic start failed: {ex.Message}"));
            SetState(DictationState.Idle);
        }
    }

    private void OnHoldEnded(HoldEnded hold)
    {
        CapturedAudio audio;
        try
        {
            audio = _audio.Stop();
        }
        catch (Exception ex)
        {
            Failed?.Invoke(new DictationFailure(DictationFailureKind.Microphone, $"capture failed: {ex.Message}"));
            SetState(DictationState.Idle);
            return;
        }

        // Nothing spoken: skip transcription entirely and dismiss instantly (Josh 2026-07-14 —
        // a quick tap or silent hold must not linger in Processing or paste a blank annotation).
        if (audio.Peak < SilencePeak)
        {
            NoSpeech?.Invoke();
            SetState(DictationState.Idle);
            return;
        }

        SetState(DictationState.Processing);
        _ = ProcessAsync(audio, hold.RawMode); // fire-and-forget; all exits handled inside
    }

    /// <summary>Skip cleanup for every dictation regardless of the Shift modifier (the raw-mode-default setting).</summary>
    public bool ForceRawMode { get; set; }

    /// <summary>Swap the cleanup client at runtime (e.g. after the API key is set/changed in settings). Null = raw.</summary>
    public void SetCleanup(ICleanupClient? cleanup) => _cleanup = cleanup;

    private async Task ProcessAsync(CapturedAudio audio, bool rawMode)
    {
        var raw = rawMode || ForceRawMode;
        var total = Stopwatch.StartNew();
        try
        {
            var transcribeWatch = Stopwatch.StartNew();
            string text;
            using (var stallCts = new CancellationTokenSource(TranscriptionStallTimeout))
            {
                try
                {
                    text = await _transcriber.TranscribeAsync(audio, stallCts.Token);
                }
                catch (OperationCanceledException) when (stallCts.IsCancellationRequested)
                {
                    // A hung transcription — cancelled by the watchdog. Degrade to an overlay error
                    // instead of leaving the pill frozen in Processing forever.
                    Failed?.Invoke(new DictationFailure(DictationFailureKind.Transcription,
                        $"transcription stalled (>{TranscriptionStallTimeout.TotalSeconds:F0}s)"));
                    return;
                }
            }
            transcribeWatch.Stop();

            // Whisper returns "[BLANK_AUDIO]" / "(silence)" etc. for near-silent input — never paste those.
            if (TranscriptFilters.IsBlank(text))
            {
                NoSpeech?.Invoke();
                return;
            }

            // Cleanup pass (spec §4). Raw mode (Shift or the default setting) or no configured
            // client skips it; the cleanup client itself never throws — timeout/offline → raw text.
            var cleanupWatch = Stopwatch.StartNew();
            var cleanup = (raw || _cleanup is null)
                ? CleanupResult.Raw(text, raw ? "raw mode" : "no cleanup client")
                : await _cleanup.CleanAsync(text);
            cleanupWatch.Stop();

            var injectWatch = Stopwatch.StartNew();
            var result = await _injector.InjectAsync(cleanup.Text);
            injectWatch.Stop();
            total.Stop();

            Completed?.Invoke(new DictationReport(
                result.Text, result.Outcome, raw, cleanup.WasCleaned,
                audio.Duration, transcribeWatch.Elapsed, cleanupWatch.Elapsed, injectWatch.Elapsed, total.Elapsed,
                cleanup.FallbackReason));
        }
        catch (Exception ex)
        {
            Failed?.Invoke(new DictationFailure(DictationFailureKind.Transcription, $"dictation failed: {ex.Message}"));
        }
        finally
        {
            SetState(DictationState.Idle);
        }
    }

    private void SetState(DictationState state)
    {
        State = state;
        StateChanged?.Invoke(state);
    }

    public void Dispose()
    {
        _hotkeys.HoldStarted -= OnHoldStarted;
        _hotkeys.HoldEnded -= OnHoldEnded;
    }
}
