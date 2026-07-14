using System.Media;
using System.Windows;
using Outspoken.App.Overlay;
using Outspoken.Core;
using Outspoken.Core.Audio;
using Outspoken.Core.Cleanup;
using Outspoken.Core.Hotkeys;
using Outspoken.Core.Injection;
using Outspoken.Core.Orchestration;
using Outspoken.Core.Transcription;

namespace Outspoken.App;

/// <summary>
/// Debug shell around the T7 orchestrator. Feedback is a system sound + log lines for
/// now (per plan); the overlay pill replaces this at T9.
/// </summary>
public partial class MainWindow : Window
{
    private readonly KeyboardHookService _hook = new();
    private readonly WasapiAudioCaptureService _audio = new();
    private readonly PillWindow _pill = new();
    private WhisperTranscriber? _transcriber;
    private DictationOrchestrator? _orchestrator;

    public MainWindow()
    {
        InitializeComponent();
        Closed += (_, _) =>
        {
            _orchestrator?.Dispose();
            _hook.Dispose();
            _audio.Dispose();
            _transcriber?.Dispose();
            _pill.Close();
        };
        Loaded += async (_, _) => await InitAsync();
    }

    private async Task InitAsync()
    {
        try
        {
            var expectedModel = System.IO.Path.Combine(WhisperModelStore.DefaultModelDirectory, WhisperModelStore.ModelFileName);
            Log($"model path: {expectedModel} | exists: {System.IO.File.Exists(expectedModel)}");
            var progress = new Progress<double>(p => Log($"  model download {p:P0}"));
            _transcriber = await WhisperTranscriber.CreateAsync(downloadProgress: progress);
            Log($"✓ model warm — load took {_transcriber.ModelLoadTime.TotalMilliseconds:F0} ms");
            _transcriber.ProcessorRebuilt += reason => Log($"♻ whisper processor rebuilt ({reason})");

            // A processor that lived through sleep comes back ~10x degraded (dogfood 2026-07-14).
            Microsoft.Win32.SystemEvents.PowerModeChanged += (_, e) =>
            {
                if (e.Mode == Microsoft.Win32.PowerModes.Resume)
                {
                    _transcriber?.Rebuild();
                    Log("♻ system resumed — whisper processor rebuild queued");
                }
            };

            // Cleanup client (spec §4): only if Josh has stored his API key via `set-key`.
            // No key → raw transcripts, which still work — cleanup is an enhancement, not a gate.
            ICleanupClient? cleanup = null;
            var apiKey = ApiKeyStore.TryLoad();
            if (apiKey is not null)
            {
                var anthropic = new AnthropicCleanupClient(apiKey);
                cleanup = anthropic;
                Log($"✓ cleanup enabled — {CoreInfo.CleanupModel}, {CoreInfo.CleanupTimeoutMs}ms timeout → raw fallback");

                // Warm the HTTPS connection so the first dictation isn't ~11s cold (dogfood 2026-07-14).
                _ = Task.Run(async () =>
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    await anthropic.WarmUpAsync();
                    Log($"✓ cleanup connection warm ({sw.Elapsed.TotalSeconds:F1}s) — first dictation is now fast");
                });
            }
            else
            {
                Log("• cleanup OFF — no API key stored. Run: Outspoken.App.exe set-key  (dictation still works, raw)");
            }

            _orchestrator = new DictationOrchestrator(_hook, _audio, _transcriber, new InjectionEngine(new Win32InjectionEnvironment()), cleanup);
            _orchestrator.StateChanged += OnStateChanged;
            _orchestrator.Completed += OnCompleted;
            _orchestrator.Failed += m =>
            {
                Dispatcher.BeginInvoke(() => _pill.ShowError(m.StartsWith('(') ? "no speech heard" : "dictation error"));
                Log($"✗ {m}");
            };
            Log("✓ ready — hold Ctrl+Win anywhere and speak (+Shift = raw mode, skips cleanup)");
        }
        catch (Exception ex)
        {
            Log($"✗ init failed: {ex.Message}");
        }
    }

    private void OnStateChanged(DictationState state)
    {
        // StateChanged arrives on the hook/worker thread; the pill lives on the UI thread.
        Dispatcher.BeginInvoke(() =>
        {
            switch (state)
            {
                case DictationState.Listening:
                    _pill.ShowListening(() => _audio.CurrentLevel);
                    SystemSounds.Exclamation.Play(); // placeholder cue; real soft ticks land at T10
                    break;
                case DictationState.Processing:
                    _pill.ShowProcessing();
                    break;
            }
        });
        Log($"· {state}");
    }

    private void OnCompleted(DictationReport r)
    {
        Dispatcher.BeginInvoke(() =>
        {
            SystemSounds.Asterisk.Play();
            if (r.Outcome is InjectionOutcome.CopiedToClipboard or InjectionOutcome.InjectedWithoutRestore)
                _pill.ShowClipboard();
            else
                _pill.ShowDone(rawMode: r.RawMode || !r.WasCleaned);
        });

        var tag = r.RawMode ? "  [RAW]" : r.WasCleaned ? "  [cleaned]" : $"  [raw fallback: {r.CleanupFallbackReason}]";
        Log($"„ {r.Text}{tag}");
        Log($"⏱ release→done {r.TotalFromRelease.TotalSeconds:F2}s " +
            $"(transcribe {r.TranscribeTime.TotalSeconds:F2}s + cleanup {r.CleanupTime.TotalSeconds:F2}s + inject {r.InjectTime.TotalSeconds:F2}s) " +
            $"| audio {r.AudioDuration.TotalSeconds:F1}s | {r.Outcome}");
    }

    private void Log(string line)
    {
        Dispatcher.BeginInvoke(() =>
        {
            EventLog.Items.Add($"{DateTime.Now:HH:mm:ss.fff}  {line}");
            EventLog.ScrollIntoView(EventLog.Items[^1]);
        });
    }
}
