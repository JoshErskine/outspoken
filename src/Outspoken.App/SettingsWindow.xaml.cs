using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using Outspoken.App.Startup;
using Outspoken.Core.Cleanup;
using Outspoken.Core.Settings;

namespace Outspoken.App;

/// <summary>
/// The single-page settings window (Design Direction §Settings). Reads current state on open,
/// applies changes to the running <see cref="DictationHost"/> and persists them on Save. The
/// API key is written to the DPAPI store (never here in plaintext beyond the PasswordBox).
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsStore _store;
    private readonly DictationHost _host;

    public SettingsWindow(SettingsStore store, DictationHost host)
    {
        InitializeComponent();
        _store = store;
        _host = host;

        // The app-icon squircle mark (cream ground) reads cleanly against the settings surface.
        LogoHost.Content = Overlay.BrandMark.CreateVisual(withGround: true);

        var settings = store.Load();
        CuesCheck.IsChecked = settings.AudioCuesEnabled;
        RawDefaultCheck.IsChecked = settings.RawModeDefault;
        LaunchCheck.IsChecked = StartupRegistration.IsEnabled();
        VocabularyBox.Text = settings.CustomVocabulary;

        var hasKey = ApiKeyStore.Exists;
        KeyStatus.Text = hasKey ? "✓ A key is configured." : "No key configured — cleanup is off.";
        KeyStatus.Foreground = new SolidColorBrush(hasKey
            ? Color.FromRgb(0x3B, 0x7A, 0x3B)   // muted green
            : Color.FromRgb(0x9A, 0x89, 0x7B));  // muted ink
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        try
        {
            // Persist the toggles.
            var settings = new AppSettings
            {
                AudioCuesEnabled = CuesCheck.IsChecked == true,
                RawModeDefault = RawDefaultCheck.IsChecked == true,
                LaunchAtLogin = LaunchCheck.IsChecked == true,
                CustomVocabulary = VocabularyBox.Text.Trim(),
            };
            _store.Save(settings);
            _host.ApplySettings(settings);

            // Launch-at-login registry entry.
            StartupRegistration.SetEnabled(settings.LaunchAtLogin, Environment.ProcessPath!);

            // API key: only touch the store if the user typed a new one.
            var newKey = ApiKeyBox.Password;
            if (!string.IsNullOrWhiteSpace(newKey))
            {
                ApiKeyStore.Save(newKey);
                _host.ReloadCleanup();
            }

            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't save settings: {ex.Message}", "Outspoken",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    // Drag the window from anywhere that isn't an interactive control. Buttons, toggles, and the
    // key box mark their own mouse-down handled, so the event only bubbles here from inert areas
    // (header, card backgrounds, section labels, gaps).
    private void OnWindowDrag(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
            DragMove();
    }
}
