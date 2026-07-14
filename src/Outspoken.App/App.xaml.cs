using System.Windows;
using Outspoken.Core.Cleanup;

namespace Outspoken.App;

/// <summary>
/// Interaction logic for App.xaml. Also handles the `set-key` guided command
/// (spec §4 / ADR-001): stores Josh's Anthropic API key encrypted via DPAPI.
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Length > 0 && e.Args[0].Equals("set-key", StringComparison.OrdinalIgnoreCase))
        {
            RunSetKey();
            Shutdown();
            return;
        }

        base.OnStartup(e);
        new MainWindow().Show();
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
