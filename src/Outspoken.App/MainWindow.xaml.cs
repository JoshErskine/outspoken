using System.Media;
using System.Windows;
using Outspoken.Core.Audio;
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

            _orchestrator = new DictationOrchestrator(_hook, _audio, _transcriber, new InjectionEngine(new Win32InjectionEnvironment()));
            _orchestrator.StateChanged += OnStateChanged;
            _orchestrator.Completed += OnCompleted;
            _orchestrator.Failed += m => Log($"✗ {m}");
            Log("✓ ready — hold Ctrl+Win anywhere and speak (+Shift = raw mode)");
        }
        catch (Exception ex)
        {
            Log($"✗ init failed: {ex.Message}");
        }
    }

    private void OnStateChanged(DictationState state)
    {
        if (state == DictationState.Listening)
            SystemSounds.Exclamation.Play(); // placeholder cue; real soft ticks land at T10
        Log($"· {state}");
    }

    private void OnCompleted(DictationReport r)
    {
        SystemSounds.Asterisk.Play();
        Log($"„ {r.Text}{(r.RawMode ? "  [RAW]" : "")}");
        Log($"⏱ release→done {r.TotalFromRelease.TotalSeconds:F2}s " +
            $"(transcribe {r.TranscribeTime.TotalSeconds:F2}s + inject {r.InjectTime.TotalSeconds:F2}s) " +
            $"| audio {r.AudioDuration.TotalSeconds:F1}s | {r.Outcome} | target ≤1.5s {(r.TotalFromRelease.TotalSeconds <= 1.5 ? "✅" : "❌")}");
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
