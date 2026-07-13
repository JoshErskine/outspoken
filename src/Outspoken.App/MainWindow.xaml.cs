using System.Windows;
using Outspoken.Core.Audio;
using Outspoken.Core.Hotkeys;

namespace Outspoken.App;

/// <summary>
/// Temporary debug harness for the T3/T4 manual verifies. Replaced by the overlay pill (T9);
/// the wiring moves to an orchestrator at T7.
/// </summary>
public partial class MainWindow : Window
{
    private readonly KeyboardHookService _hook;
    private readonly WasapiAudioCaptureService _audio = new();

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
        };
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
            var peak = audio.Samples.Length > 0 ? audio.Samples.Max(MathF.Abs) : 0f;
            Log($"▲ hold ended — {e.Duration.TotalMilliseconds:F0} ms{(e.RawMode ? " [RAW]" : "")} | " +
                $"captured {audio.Duration.TotalSeconds:F2}s @ {audio.SampleRate}Hz mono, {audio.Samples.Length} samples, peak {peak:F3} — mic released");
        }
        catch (Exception ex)
        {
            Log($"✗ capture stop failed: {ex.Message}");
        }
    }

    private void Log(string line)
    {
        // Hook events arrive on the hook thread; ListBox lives on the UI thread.
        Dispatcher.BeginInvoke(() =>
        {
            EventLog.Items.Add($"{DateTime.Now:HH:mm:ss.fff}  {line}");
            EventLog.ScrollIntoView(EventLog.Items[^1]);
        });
    }
}
