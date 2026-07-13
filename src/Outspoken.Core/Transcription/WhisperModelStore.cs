namespace Outspoken.Core.Transcription;

/// <summary>
/// Locates the ggml model file, downloading it on first run (ADR-002: base.en-q5_1,
/// ~57MB, from the upstream whisper.cpp Hugging Face repo). Models live in a
/// models/ folder beside the executable — the only thing Outspoken ever writes
/// to disk; dictation audio and text never land here (ADR-001).
/// </summary>
public static class WhisperModelStore
{
    public const string ModelFileName = "ggml-base.en-q5_1.bin";
    public const string ModelUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/" + ModelFileName;

    public static string DefaultModelDirectory => Path.Combine(AppContext.BaseDirectory, "models");

    /// <summary>Returns the model path, downloading first if absent. Progress is 0..1.</summary>
    public static async Task<string> EnsureModelAsync(
        string? modelDirectory = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var dir = modelDirectory ?? DefaultModelDirectory;
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, ModelFileName);
        if (File.Exists(path))
            return path;

        // Download to a temp name then move, so a crash mid-download can't leave a
        // half-written file that later loads as a corrupt model.
        var tempPath = path + ".partial";
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        using var response = await http.GetAsync(ModelUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;
        await using (var src = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var dst = File.Create(tempPath))
        {
            var buffer = new byte[81920];
            long readTotal = 0;
            int read;
            while ((read = await src.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                readTotal += read;
                if (totalBytes is { } total)
                    progress?.Report((double)readTotal / total);
            }
        }

        File.Move(tempPath, path, overwrite: true);
        return path;
    }
}
