using System.IO;
using Outspoken.Core.Settings;

namespace Outspoken.Core.Tests;

public class SettingsStoreTests
{
    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"outspoken-settings-{Guid.NewGuid():N}.json");

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var store = new SettingsStore(TempPath());
        var s = store.Load();

        Assert.True(s.AudioCuesEnabled);   // on by default
        Assert.False(s.RawModeDefault);
        Assert.False(s.LaunchAtLogin);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsAllFields()
    {
        var path = TempPath();
        try
        {
            var store = new SettingsStore(path);
            store.Save(new AppSettings { AudioCuesEnabled = false, RawModeDefault = true, LaunchAtLogin = true });

            var loaded = new SettingsStore(path).Load();
            Assert.False(loaded.AudioCuesEnabled);
            Assert.True(loaded.RawModeDefault);
            Assert.True(loaded.LaunchAtLogin);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_CorruptFile_ReturnsDefaults()
    {
        var path = TempPath();
        try
        {
            File.WriteAllText(path, "{ this is not valid json");
            var s = new SettingsStore(path).Load();
            Assert.True(s.AudioCuesEnabled); // defaults, not a crash
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void DefaultPath_IsUnderLocalAppData()
    {
        var store = new SettingsStore();
        Assert.Contains(Path.Combine("Outspoken", "settings.json"), store.Path);
    }
}
