using Outspoken.Core.Transcription;

namespace Outspoken.Core.Tests;

public class WhisperModelStoreTests
{
    [Fact]
    public void DefaultModelDirectory_IsStableAppDataPath_NotBinFolder()
    {
        var dir = WhisperModelStore.DefaultModelDirectory;
        Assert.Contains(Path.Combine("Outspoken", "models"), dir);
        Assert.DoesNotContain("bin", dir.Replace(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ""));
    }

    /// <summary>Loads the model from the real default location — the exact path the app uses at startup.</summary>
    [SkippableFact]
    public async Task Transcriber_LoadsFromDefaultModelDirectory()
    {
        Skip.IfNot(File.Exists(Path.Combine(WhisperModelStore.DefaultModelDirectory, WhisperModelStore.ModelFileName)),
            "Model not installed in %LOCALAPPDATA%\\Outspoken\\models on this machine.");

        using var transcriber = await WhisperTranscriber.CreateAsync();
        Assert.True(transcriber.ModelLoadTime > TimeSpan.Zero);
    }
}
