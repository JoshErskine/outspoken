using Microsoft.Win32;

namespace Outspoken.App.Startup;

/// <summary>
/// Launch-at-login via the per-user HKCU Run key — no admin rights, fully reversible, and it
/// only affects this Windows user (Design Direction §Settings; ADR privacy — nothing machine-wide).
/// </summary>
public static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Outspoken";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is not null;
    }

    /// <summary>Adds or removes the Run entry pointing at the current executable.</summary>
    public static void SetEnabled(bool enabled, string exePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enabled)
            key.SetValue(ValueName, $"\"{exePath}\"");
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
