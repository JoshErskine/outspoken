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

    protected override void OnStartup(StartupEventArgs e)
    {
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

    /// <summary>Draws the amber-dot tray icon directly (no asset, no async conversion).</summary>
    private static Icon BuildTrayIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(System.Drawing.Color.Transparent);
            using var brush = new SolidBrush(System.Drawing.Color.FromArgb(0xC2, 0x57, 0x1B));
            g.FillEllipse(brush, 4, 4, 24, 24);
        }
        return Icon.FromHandle(bmp.GetHicon());
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
