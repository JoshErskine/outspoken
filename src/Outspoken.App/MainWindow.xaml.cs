using System.Windows;

namespace Outspoken.App;

/// <summary>
/// Diagnostics log viewer — a hidden window surfaced from the tray ("Diagnostics"). It no
/// longer owns the dictation engine (that moved to <see cref="DictationHost"/>); it just
/// displays log lines. Closing hides it rather than shutting the app down.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // Tray app: closing the diagnostics window just hides it — unless the user chose Quit.
        Closing += (_, e) =>
        {
            if (!((App)Application.Current).Quitting)
            {
                e.Cancel = true;
                Hide();
            }
        };
    }

    public void Append(string line)
    {
        Dispatcher.BeginInvoke(() =>
        {
            EventLog.Items.Add($"{DateTime.Now:HH:mm:ss.fff}  {line}");
            EventLog.ScrollIntoView(EventLog.Items[^1]);
        });
    }
}
