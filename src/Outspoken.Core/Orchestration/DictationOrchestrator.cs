using System.Diagnostics;
using Outspoken.Core.Audio;
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
    TimeSpan AudioDuration,
    TimeSpan TranscribeTime,
    TimeSpan InjectTime,
    TimeSpan TotalFromRelease);

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

    public DictationOrchestrator(IHotkeySource hotkeys, IAudioCaptureService audio, ITranscriber transcriber, IInjector injector)
    {
        _hotkeys = hotkeys;
        _audio = audio;
        _transcriber = transcriber;
        _injector = injector;
        _hotkeys.HoldStarted += OnHoldStarted;
        _hotkeys.HoldEnded += OnHoldEnded;
    }

    public DictationState State { get; private set; } = DictationState.Idle;

    public event Action<DictationState>? StateChanged;
    public event Action<DictationReport>? Completed;

    /// <summary>A stage failed. The string is operator-facing; dictation text (when any survived) rode the injection result.</summary>
    public event Action<string>? Failed;

    private void OnHoldStarted()
    {
        try
        {
            _audio.Start();
            SetState(DictationState.Listening);
        }
        catch (Exception ex)
        {
            Failed?.Invoke($"mic start failed: {ex.Message}");
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
            Failed?.Invoke($"capture failed: {ex.Message}");
            SetState(DictationState.Idle);
            return;
        }

        SetState(DictationState.Processing);
        _ = ProcessAsync(audio, hold.RawMode); // fire-and-forget; all exits handled inside
    }

    private async Task ProcessAsync(CapturedAudio audio, bool rawMode)
    {
        var total = Stopwatch.StartNew();
        try
        {
            var transcribeWatch = Stopwatch.StartNew();
            var text = await _transcriber.TranscribeAsync(audio);
            transcribeWatch.Stop();

            if (text.Length == 0)
            {
                Failed?.Invoke("(silence — nothing transcribed)");
                return;
            }

            // Raw vs cleaned diverges at T8 (cleanup client); today everything is raw.
            var injectWatch = Stopwatch.StartNew();
            var result = await _injector.InjectAsync(text);
            injectWatch.Stop();
            total.Stop();

            Completed?.Invoke(new DictationReport(
                result.Text, result.Outcome, rawMode,
                audio.Duration, transcribeWatch.Elapsed, injectWatch.Elapsed, total.Elapsed));
        }
        catch (Exception ex)
        {
            Failed?.Invoke($"dictation failed: {ex.Message}");
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
