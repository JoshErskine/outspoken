using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using Outspoken.App.Overlay;
using Outspoken.Core.Cleanup;
using Outspoken.Core.Settings;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace Outspoken.App;

/// <summary>
/// Application entry. Runs as a system-tray app (no main window): the tray icon hosts
/// Settings and Quit, and a hidden diagnostics window is available on demand. Also handles
/// the `set-key` and `showcase` command-line utilities.
/// </summary>
public partial class App : Application
{
    private readonly SettingsStore _settingsStore = new();
    private DictationHost? _host;
    private NotifyIcon? _tray;
    private MainWindow? _diagnostics;

    /// <summary>True once the user chose Quit — lets the diagnostics window actually close.</summary>
    internal bool Quitting { get; private set; }

    // Single-instance guard: a pinned/autostart tray app must never run twice (two hooks = chaos).
    private System.Threading.Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Keep this process off Windows' efficiency mode - an idle tray app gets throttled, and the
        // first Whisper transcription after idle otherwise crawls (T12). Applies to every entry path.
        ProcessPerformance.DisableThrottling();

        if (e.Args.Length > 0 && e.Args[0].Equals("set-key", StringComparison.OrdinalIgnoreCase))
        {
            RunSetKey();
            Shutdown();
            return;
        }

        if (e.Args.Length > 0 && e.Args[0].Equals("showcase", StringComparison.OrdinalIgnoreCase))
        {
            var outputDir = e.Args.Length > 1 ? e.Args[1] : Directory.GetCurrentDirectory();
            ShowcaseRenderer.RenderAll(outputDir);
            Shutdown();
            return;
        }

        if (e.Args.Length > 1 && e.Args[0].Equals("appicon", StringComparison.OrdinalIgnoreCase))
        {
            WriteAppIcon(e.Args[1]);
            Shutdown();
            return;
        }

        if (e.Args.Length > 0 && e.Args[0].Equals("latency", StringComparison.OrdinalIgnoreCase))
        {
            // latency [outputFile] [runs] - measures transcription vs the spec §3 budget (T12).
            var outputPath = e.Args.Length > 1 ? e.Args[1] : Path.Combine(Path.GetTempPath(), "outspoken-latency.md");
            var runs = e.Args.Length > 2 && int.TryParse(e.Args[2], out var n) ? n : 5;
            var idle = e.Args.Length > 3 && int.TryParse(e.Args[3], out var s) ? s : 0;
            // Run off the dispatcher thread: blocking it here while TranscribeAsync's awaits try to
            // resume on the WPF SynchronizationContext would deadlock. Task.Run gives the async work
            // a thread-pool context with no captured UI scheduler.
            Task.Run(() => LatencyHarness.RunAsync(outputPath, runs, idle)).GetAwaiter().GetResult();
            Shutdown();
            return;
        }

        // If Outspoken is already running, don't start a second copy — just exit quietly.
        _singleInstance = new System.Threading.Mutex(initiallyOwned: true, "Outspoken.SingleInstance", out var isNew);
        if (!isNew)
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);

        _diagnostics = new MainWindow();
        _host = new DictationHost();
        _host.Log += line => _diagnostics.Append(line);
        _tray = BuildTray();

        var settings = _settingsStore.Load();
        _ = InitAsync(settings);
    }

    private async Task InitAsync(AppSettings settings)
    {
        await _host!.InitAsync();
        _host.ApplySettings(settings);
    }

    private NotifyIcon BuildTray()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Settings…", null, (_, _) => OpenSettings());
        menu.Items.Add("Diagnostics", null, (_, _) => OnUi(() => { _diagnostics!.Show(); _diagnostics.Activate(); }));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit Outspoken", null, (_, _) => OnUi(() => { Quitting = true; Shutdown(); }));

        var tray = new NotifyIcon
        {
            Icon = BuildTrayIcon(),
            Text = "Outspoken — hold Ctrl+Win to dictate",
            Visible = true,
            ContextMenuStrip = menu,
        };
        tray.DoubleClick += (_, _) => OnUi(OpenSettings);
        return tray;
    }

    private void OpenSettings()
    {
        foreach (Window w in Windows)
            if (w is SettingsWindow existing) { existing.Activate(); return; }
        new SettingsWindow(_settingsStore, _host!).Show();
    }

    private static void OnUi(Action action) => Application.Current.Dispatcher.BeginInvoke(action);

    /// <summary>The brand-mark tray icon — the same "Mic &amp; Halo" mark as the app icon and
    /// Settings logo. Built from a real PNG-framed .ico (not GetHicon, whose 1-bit mask squares off
    /// the rounded corners on light backgrounds) so the full alpha — including the soft squircle
    /// corners — is preserved.</summary>
    private static Icon BuildTrayIcon()
    {
        using var ms = new MemoryStream();
        WriteIco(ms, [32, 16]);
        ms.Position = 0;
        return new Icon(ms);
    }

    /// <summary>Writes the multi-size app .ico to a file.</summary>
    private static void WriteAppIcon(string path)
    {
        using var fs = File.Create(path);
        WriteIco(fs, [256, 64, 48, 32, 16]);
    }

    /// <summary>
    /// Writes a multi-size .ico with PNG-embedded frames of the brand mark. PNG frames preserve the
    /// exact colours and the full alpha channel (unlike GetHicon+Icon.Save, which flattens
    /// anti-aliased edges to a 1-bit mask). Windows Vista+ reads PNG icon frames.
    /// </summary>
    private static void WriteIco(Stream stream, int[] sizes)
    {
        var frames = sizes.Select(sz => BrandMark.RenderPng(sz, withGround: true)).ToList();

        var w = new BinaryWriter(stream);
        w.Write((short)0);            // reserved
        w.Write((short)1);            // type: icon
        w.Write((short)frames.Count); // image count

        var offset = 6 + 16 * frames.Count;
        for (var i = 0; i < frames.Count; i++)
        {
            var dim = sizes[i] >= 256 ? 0 : sizes[i]; // 0 means 256 in the ICO spec
            w.Write((byte)dim);       // width
            w.Write((byte)dim);       // height
            w.Write((byte)0);         // palette count
            w.Write((byte)0);         // reserved
            w.Write((short)1);        // colour planes
            w.Write((short)32);       // bits per pixel
            w.Write(frames[i].Length);
            w.Write(offset);
            offset += frames[i].Length;
        }
        foreach (var frame in frames)
            w.Write(frame);
        w.Flush();
    }


    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        base.OnExit(e);
    }

    /// <summary>
    /// Reads the key from the ANTHROPIC_API_KEY environment variable and DPAPI-encrypts it.
    /// The key is never typed into the app, never logged, never written in plaintext — it
    /// travels env var → memory → DPAPI blob only.
    /// </summary>
    private static void RunSetKey()
    {
        var key = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (string.IsNullOrWhiteSpace(key))
        {
            MessageBox.Show(
                "No key found in the ANTHROPIC_API_KEY environment variable.\n\n" +
                "In PowerShell, run this (paste your own key), then re-run set-key:\n\n" +
                "  $env:ANTHROPIC_API_KEY = \"sk-ant-...\"\n" +
                "  .\\Outspoken.App.exe set-key\n\n" +
                "The key is encrypted with your Windows account (DPAPI) and stored at:\n" +
                ApiKeyStore.KeyFilePath,
                "Outspoken — set API key", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            ApiKeyStore.Save(key);
            MessageBox.Show(
                $"API key stored (DPAPI-encrypted) at:\n{ApiKeyStore.KeyFilePath}\n\n" +
                "Clear the env var now so the plaintext key doesn't linger:\n" +
                "  Remove-Item Env:\\ANTHROPIC_API_KEY\n\n" +
                "Cleanup will be enabled next time you launch Outspoken.",
                "Outspoken — key saved", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to store key: {ex.Message}",
                "Outspoken — error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
