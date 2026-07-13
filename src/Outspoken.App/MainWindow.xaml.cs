using System.Diagnostics;
using System.Windows;
using Outspoken.Core.Audio;
using Outspoken.Core.Hotkeys;
using Outspoken.Core.Injection;
using Outspoken.Core.Transcription;

namespace Outspoken.App;

/// <summary>
/// Temporary debug harness for the T3/T4/T5 manual verifies. Replaced by the overlay
/// pill (T9); the wiring moves to an orchestrator at T7.
/// </summary>
public partial class MainWindow : Window
{
    private readonly KeyboardHookService _hook;
    private readonly WasapiAudioCaptureService _audio = new();
    private readonly InjectionEngine _injector = new(new Win32InjectionEnvironment());
    private WhisperTranscriber? _transcriber;

    public MainWindow()
    {
        InitializeComponent();

        _hook = new KeyboardHookService();
        _hook.HoldStarted += OnHoldStarted;
        _hook.HoldEnded += OnHoldEnded;
        Closed += (_, _) =>
        {
            _hook.Dispose();
            _audio.Dispose();
            _transcriber?.Dispose();
        };
        Loaded += async (_, _) => await InitTranscriberAsync();
    }

    private async Task InitTranscriberAsync()
    {
        try
        {
            var expectedModel = System.IO.Path.Combine(WhisperModelStore.DefaultModelDirectory, WhisperModelStore.ModelFileName);
            Log($"model path: {expectedModel} | exists: {System.IO.File.Exists(expectedModel)}");
            Log("loading Whisper model (downloads ~57MB on first run)…");
            var progress = new Progress<double>(p => Log($"  model download {p:P0}"));
            _transcriber = await WhisperTranscriber.CreateAsync(downloadProgress: progress);
            Log($"✓ model warm — load took {_transcriber.ModelLoadTime.TotalMilliseconds:F0} ms (startup cost, not per-dictation)");
        }
        catch (Exception ex)
        {
            Log($"✗ transcriber init failed: {ex.Message}");
        }
    }

    private void OnHoldStarted()
    {
        try
        {
            _audio.Start();
            Log("▼ hold started — mic open, buffering…");
        }
        catch (Exception ex)
        {
            Log($"✗ mic start failed: {ex.Message}");
        }
    }

    private void OnHoldEnded(HoldEnded e)
    {
        try
        {
            var audio = _audio.Stop();
            Log($"▲ hold ended — captured {audio.Duration.TotalSeconds:F2}s{(e.RawMode ? " [RAW]" : "")} — mic released, transcribing…");
            _ = TranscribeAsync(audio);
        }
        catch (Exception ex)
        {
            Log($"✗ capture stop failed: {ex.Message}");
        }
    }

    private async Task TranscribeAsync(CapturedAudio audio)
    {
        var transcriber = _transcriber;
        if (transcriber is null)
        {
            Log("✗ transcriber not ready yet");
            return;
        }

        try
        {
            var sw = Stopwatch.StartNew();
            var text = await transcriber.TranscribeAsync(audio);
            sw.Stop();
            Log($"„ {(text.Length > 0 ? text : "(silence)")}  [{sw.Elapsed.TotalSeconds:F2}s]");

            if (text.Length > 0)
            {
                var result = await _injector.InjectAsync(text);
                Log($"→ injection: {result.Outcome}");
            }
        }
        catch (Exception ex)
        {
            Log($"✗ transcription failed: {ex.Message}");
        }
    }

    private void Log(string line)
    {
        // Events arrive on hook/worker threads; ListBox lives on the UI thread.
        Dispatcher.BeginInvoke(() =>
        {
            EventLog.Items.Add($"{DateTime.Now:HH:mm:ss.fff}  {line}");
            EventLog.ScrollIntoView(EventLog.Items[^1]);
        });
    }
}
