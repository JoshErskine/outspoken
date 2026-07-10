using System.Diagnostics;
using System.Globalization;
using System.Text;
using NAudio.Wave;
using Whisper.net;

// Outspoken · T1 — Whisper ARM64 benchmark spike (the gate).
// Proves whisper.cpp (via Whisper.net managed bindings) runs on win-arm64 CPU
// and measures transcription latency for base.en-q5 vs small.en-q5, so ADR-002
// can lock a model + binding. See:
//   03 Projects/Outspoken/02 Planning/(C) Implementation Plan.md  (T1)
//   03 Projects/Outspoken/03 Decisions/(C) ADR-002 Whisper Runtime & UI Framework.md
//
// Usage:
//   dotnet run -c Release -- record   # capture 5s / 10s / 20s fixture clips from the mic
//   dotnet run -c Release -- models   # download base.en-q5_1 + small.en-q5_1 into models/
//   dotnet run -c Release -- bench     # transcribe each fixture with each model, print timings

var command = args.Length > 0 ? args[0].ToLowerInvariant() : "help";

var root = AppContext.BaseDirectory;
// Walk up out of bin/Release/... back to the project folder so fixtures/models
// live beside the source, not buried in build output.
var projectDir = FindProjectDir(root);
var fixturesDir = Path.Combine(projectDir, "fixtures");
var modelsDir = Path.Combine(projectDir, "models");
Directory.CreateDirectory(fixturesDir);
Directory.CreateDirectory(modelsDir);

// Fixture clips per ADR-002 §3: 5s / 10s / 20s.
var clips = new (string Name, int Seconds)[]
{
    ("clip_05s.wav", 5),
    ("clip_10s.wav", 10),
    ("clip_20s.wav", 20),
};

// Models under test — quantized English-only, per ADR-002 §2.
var models = new (string Label, string FileName, string Url)[]
{
    ("base.en-q5_1", "ggml-base.en-q5_1.bin",
        "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.en-q5_1.bin"),
    ("small.en-q5_1", "ggml-small.en-q5_1.bin",
        "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.en-q5_1.bin"),
};

switch (command)
{
    case "record": await RecordAsync(); break;
    case "models": await DownloadModelsAsync(); break;
    case "bench":  await BenchAsync();  break;
    default:       PrintHelp();         break;
}

return;

// ---------------------------------------------------------------------------

async Task RecordAsync()
{
    Console.WriteLine("== Recording fixture clips (16 kHz mono PCM) ==");
    Console.WriteLine("Speak naturally — normal dictation, the kind you'd feed an AI agent.");
    Console.WriteLine("Each clip auto-stops after its duration. Press Enter to start each one.\n");

    foreach (var (name, seconds) in clips)
    {
        Console.Write($"  {name} — {seconds}s. Press Enter, then talk... ");
        Console.ReadLine();

        var path = Path.Combine(fixturesDir, name);
        using var waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(16000, 16, 1), // whisper wants 16 kHz mono
        };
        await using var writer = new WaveFileWriter(path, waveIn.WaveFormat);
        waveIn.DataAvailable += (_, e) => writer.Write(e.Buffer, 0, e.BytesRecorded);

        waveIn.StartRecording();
        for (var s = seconds; s > 0; s--)
        {
            Console.Write($"\r    recording... {s,2}s remaining ");
            await Task.Delay(1000);
        }
        waveIn.StopRecording();
        await Task.Delay(200); // let the last DataAvailable flush
        Console.WriteLine($"\r    saved {path}                 ");
    }

    Console.WriteLine("\nDone. Now run:  dotnet run -c Release -- models   then   -- bench");
}

async Task DownloadModelsAsync()
{
    using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
    foreach (var (label, fileName, url) in models)
    {
        var dest = Path.Combine(modelsDir, fileName);
        if (File.Exists(dest))
        {
            Console.WriteLine($"  {label}: already present ({new FileInfo(dest).Length / (1024 * 1024)} MB)");
            continue;
        }

        Console.WriteLine($"  {label}: downloading {url}");
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        await using var src = await response.Content.ReadAsStreamAsync();
        await using var dst = File.Create(dest);
        await src.CopyToAsync(dst);
        Console.WriteLine($"    saved {dest} ({new FileInfo(dest).Length / (1024 * 1024)} MB)");
    }
}

async Task BenchAsync()
{
    var missingFixtures = clips.Where(c => !File.Exists(Path.Combine(fixturesDir, c.Name))).ToList();
    if (missingFixtures.Count > 0)
    {
        Console.WriteLine("Missing fixtures — run `record` first: " +
            string.Join(", ", missingFixtures.Select(c => c.Name)));
        return;
    }

    var rows = new List<string>
    {
        "| Model | Threads | Load (cold) | Clip | Audio | Transcribe | RTF | 10s ≤1.0s? |",
        "|---|---|---|---|---|---|---|---|",
    };

    // whisper.cpp pads every clip to a 30s encoder window, so the encoder is a
    // fixed cost dominated by thread count on this CPU-only ARM64 machine.
    // Sweep a few thread counts to find the config that meets the budget.
    var cores = Environment.ProcessorCount;
    var threadCounts = new[] { 4, 8, cores }.Distinct().Where(t => t <= cores).OrderBy(t => t).ToArray();

    Console.WriteLine($"Runtime: {System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier} | " +
                      $"OSArch: {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture} | " +
                      $"ProcArch: {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture} | " +
                      $"Cores: {cores} | Thread sweep: {string.Join(",", threadCounts)}\n");

    foreach (var (label, fileName, _) in models)
    {
        var modelPath = Path.Combine(modelsDir, fileName);
        if (!File.Exists(modelPath))
        {
            Console.WriteLine($"  {label}: model missing — run `models`. Skipping.");
            continue;
        }

        Console.WriteLine($"== {label} ==");

        // Cold start = factory load (mmaps + loads the model into memory), measured once.
        var load = Stopwatch.StartNew();
        using var factory = WhisperFactory.FromPath(modelPath);
        load.Stop();
        Console.WriteLine($"  model load: {load.ElapsedMilliseconds} ms");

        foreach (var threads in threadCounts)
        {
            using var processor = factory.CreateBuilder().WithLanguage("en").WithThreads(threads).Build();
            Console.WriteLine($"  -- {threads} threads --");

            foreach (var (name, seconds) in clips)
            {
                var wavPath = Path.Combine(fixturesDir, name);
                var sb = new StringBuilder();

                await using var fs = File.OpenRead(wavPath);
                var sw = Stopwatch.StartNew();
                await foreach (var seg in processor.ProcessAsync(fs))
                    sb.Append(seg.Text);
                sw.Stop();

                var secs = sw.Elapsed.TotalSeconds;
                var rtf = secs / seconds; // <1 means faster than realtime
                var pass = seconds == 10 ? (secs <= 1.0 ? "✅" : "❌") : "—";
                Console.WriteLine($"    {name} ({seconds}s): {secs:F2}s  (RTF {rtf:F2})");
                Console.WriteLine($"      → {sb.ToString().Trim()}");

                rows.Add($"| {label} | {threads} | {load.ElapsedMilliseconds} ms | {name} | {seconds}s | " +
                         $"{secs.ToString("F2", CultureInfo.InvariantCulture)}s | " +
                         $"{rtf.ToString("F2", CultureInfo.InvariantCulture)} | {pass} |");
            }
        }

        Console.WriteLine();
    }

    Console.WriteLine("\n--- copy into Build Log ---\n");
    foreach (var r in rows) Console.WriteLine(r);
    Console.WriteLine("\nGate: pick the largest model + lowest thread count whose 10s clip is ≤1.0s (ADR-002 §3).");
    Console.WriteLine("NOTE: silent fixtures understate real latency (decoder does ~no work on blank audio).");
}

void PrintHelp()
{
    Console.WriteLine("Outspoken · Whisper ARM64 benchmark (T1)");
    Console.WriteLine("  dotnet run -c Release -- record   capture 5s/10s/20s mic fixtures");
    Console.WriteLine("  dotnet run -c Release -- models   download base.en-q5_1 + small.en-q5_1");
    Console.WriteLine("  dotnet run -c Release -- bench     transcribe fixtures, print timing table");
}

static string FindProjectDir(string start)
{
    var dir = new DirectoryInfo(start);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WhisperBenchmark.csproj")))
        dir = dir.Parent;
    return dir?.FullName ?? start;
}
