using System.Diagnostics;
using System.IO;
using System.Windows;
using Outspoken.App.Overlay;
using Outspoken.Core;
using Outspoken.Core.Audio;
using Outspoken.Core.Cleanup;
using Outspoken.Core.Diagnostics;
using Outspoken.Core.Hotkeys;
using Outspoken.Core.Injection;
using Outspoken.Core.Orchestration;
using Outspoken.Core.Settings;
using Outspoken.Core.Transcription;

namespace Outspoken.App;

/// <summary>
/// The headless dictation engine — owns the hotkey hook, capture, transcriber, cleanup,
/// injector, overlay pill, and cue sounds, and wires them into the orchestrator. Lives for
/// the app's lifetime behind the tray icon (no window). Raises <see cref="Log"/> for the
/// optional diagnostics view; UI marshaling uses the application dispatcher.
/// </summary>
public sealed class DictationHost : IDisposable
{
    private readonly KeyboardHookService _hook = new();
    private readonly WasapiAudioCaptureService _audio = new();
    private readonly AudioCuePlayer _cues = new(LoadCue("cue-start.wav"), LoadCue("cue-stop.wav"));
    private readonly PillWindow _pill = new();
    // Errors-only local log (spec §5). Operator-facing failures only - never dictation content.
    private readonly ErrorLog _errorLog = new();
    private WhisperTranscriber? _transcriber;
    private AnthropicCleanupClient? _cleanup;
    private DictationOrchestrator? _orchestrator;

    public event Action<string>? Log;

    /// <summary>Reflects whether a valid API key is configured and cleanup is active.</summary>
    public bool CleanupEnabled => _cleanup is not null;

    public async Task InitAsync(AppSettings settings)
    {
        try
        {
            var progress = new Progress<double>(p => Emit($"model download {p:P0}"));
            _transcriber = await WhisperTranscriber.CreateAsync(downloadProgress: progress, vocabulary: settings.CustomVocabulary);
            Emit($"model warm — load {_transcriber.ModelLoadTime.TotalMilliseconds:F0} ms");
            _transcriber.ProcessorRebuilt += reason => { Emit($"whisper processor rebuilt ({reason})"); _errorLog.Write($"whisper processor rebuilt ({reason})"); };

            // A processor that lived through sleep comes back ~10x degraded (dogfood 2026-07-14).
            Microsoft.Win32.SystemEvents.PowerModeChanged += (_, e) =>
            {
                if (e.Mode == Microsoft.Win32.PowerModes.Resume)
                {
                    _transcriber?.Rebuild();
                    Emit("system resumed — whisper processor rebuild queued");
                }
            };

            _orchestrator = new DictationOrchestrator(
                _hook, _audio, _transcriber, new InjectionEngine(new Win32InjectionEnvironment()));
            _orchestrator.StateChanged += OnStateChanged;
            _orchestrator.Completed += OnCompleted;
            _orchestrator.NoSpeech += () => { OnUi(() => _pill.Dismiss()); Emit("(no speech — dismissed)"); };
            _orchestrator.Failed += m => { OnUi(() => _pill.ShowError("dictation error")); Emit($"error: {m}"); _errorLog.Write($"dictation error: {m}"); };

            ReloadCleanup();
            Emit("ready");
        }
        catch (Exception ex)
        {
            Emit($"init failed: {ex.Message}");
            _errorLog.Write($"init failed: {ex.Message}");
        }
    }

    /// <summary>Re-reads the DPAPI key and (re)builds the cleanup client — call after the key changes.</summary>
    public void ReloadCleanup()
    {
        var apiKey = ApiKeyStore.TryLoad();
        _cleanup?.Dispose();
        _cleanup = apiKey is not null ? new AnthropicCleanupClient(apiKey) : null;
        if (_orchestrator is not null)
            _orchestrator.SetCleanup(_cleanup);

        if (_cleanup is not null)
        {
            Emit($"cleanup enabled — {CoreInfo.CleanupModel}");
            var anthropic = _cleanup;
            _ = Task.Run(async () =>
            {
                var sw = Stopwatch.StartNew();
                await anthropic.WarmUpAsync();
                Emit($"cleanup connection warm ({sw.Elapsed.TotalSeconds:F1}s)");
            });
        }
        else
        {
            Emit("cleanup off — no API key; dictation runs raw");
        }
    }

    public void ApplySettings(AppSettings settings)
    {
        _cues.Enabled = settings.AudioCuesEnabled;
        if (_orchestrator is not null)
            _orchestrator.ForceRawMode = settings.RawModeDefault;
        _transcriber?.UpdateVocabulary(settings.CustomVocabulary); // takes effect next dictation
    }

    private void OnStateChanged(DictationState state)
    {
        OnUi(() =>
        {
            switch (state)
            {
                case DictationState.Listening:
                    _pill.ShowListening(() => _audio.CurrentLevel);
                    _cues.PlayStart();
                    break;
                case DictationState.Processing:
                    _pill.ShowProcessing();
                    _cues.PlayStop();
                    break;
            }
        });
    }

    private void OnCompleted(DictationReport r)
    {
        OnUi(() =>
        {
            if (r.Outcome is InjectionOutcome.CopiedToClipboard or InjectionOutcome.InjectedWithoutRestore)
                _pill.ShowClipboard();
            else
                _pill.ShowDone(rawMode: r.RawMode || !r.WasCleaned);
        });
        var tag = r.RawMode ? "[raw]" : r.WasCleaned ? "[cleaned]" : "[raw fallback]";
        Emit($"„ {r.Text} {tag} ({r.TotalFromRelease.TotalSeconds:F2}s)");
        // Per-segment budget breakdown (spec §3) - the live half of the T12 latency harness.
        foreach (var line in LatencyBudget.Evaluate(r))
            Emit($"    {line.Name,-12} {line.Measured.TotalSeconds,5:F2}s / {line.Limit.TotalSeconds:F2}s  {(line.Over ? "OVER" : "ok")}");

        // A cleanup that was wanted but fell back to raw due to a real failure (bad key, timeout,
        // 5xx) is worth the error log - it points at an API problem. Log the REASON only, never the
        // transcript. Expected fallbacks (offline, no key configured) are not errors, so skip them.
        if (!r.RawMode && !r.WasCleaned && r.CleanupFallbackReason is { } reason
            && !reason.Contains("offline") && !reason.Contains("no cleanup client"))
            _errorLog.Write($"cleanup fallback: {reason}");
    }

    private static void OnUi(Action action) => Application.Current.Dispatcher.BeginInvoke(action);
    private void Emit(string line) => Log?.Invoke(line);

    public void Dispose()
    {
        _orchestrator?.Dispose();
        _hook.Dispose();
        _audio.Dispose();
        _cues.Dispose();
        _cleanup?.Dispose();
        _transcriber?.Dispose();
        _pill.Close();
    }

    private static byte[] LoadCue(string logicalName)
    {
        using var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(logicalName)
            ?? throw new InvalidOperationException($"Embedded cue asset '{logicalName}' not found.");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
