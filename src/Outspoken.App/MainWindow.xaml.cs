using System.Windows;
using Outspoken.Core.Hotkeys;

namespace Outspoken.App;

/// <summary>
/// Temporary debug harness for the T3 manual verify. Replaced by the overlay pill (T9);
/// the hook wiring moves to an orchestrator at T7.
/// </summary>
public partial class MainWindow : Window
{
    private readonly KeyboardHookService _hook;

    public MainWindow()
    {
        InitializeComponent();

        _hook = new KeyboardHookService();
        _hook.HoldStarted += () => Log("▼ hold started — listening…");
        _hook.HoldEnded += e => Log($"▲ hold ended — {e.Duration.TotalMilliseconds:F0} ms{(e.RawMode ? "  [RAW]" : "")}");
        Closed += (_, _) => _hook.Dispose();
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
