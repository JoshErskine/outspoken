using System.Globalization;
using System.IO;
using System.Text;
using NAudio.Wave;
using Outspoken.Core.Audio;
using Outspoken.Core.Orchestration;
using Outspoken.Core.Transcription;

namespace Outspoken.App;

/// <summary>
/// The latency harness (T12, spec §8): measures the transcription segment against the spec §3
/// budget on the committed fixture clips, using the real <see cref="WhisperTranscriber"/> - so the
/// numbers reflect the production path (and track any decode-tuning we apply). Run via
/// <c>Outspoken.App.exe latency [outputFile] [runs]</c>; writes a markdown report to the file
/// (reliable output from a WinExe) and echoes to the console.
/// </summary>
public static class LatencyHarness
{
    private sealed record Clip(string FileName, string? ExpectedFile);

    private static readonly Clip[] Clips =
    {
        new("clip_05s.wav", null),
        new("clip_10s.wav", "clip_10s.expected.txt"),
        new("clip_20s.wav", null),
    };

    public static async Task RunAsync(string outputPath, int runs, int idleSeconds = 0)
    {
        var fixturesDir = FindFixturesDir();
        var sb = new StringBuilder();
        void Line(string s) { Console.WriteLine(s); Console.Out.Flush(); sb.AppendLine(s); }
        // Live progress: console only (flushed immediately), so a slow/stuck run is visible mid-flight.
        static void Progress(string s) { Console.WriteLine(s); Console.Out.Flush(); }

        Line($"# Outspoken latency harness - {DateTime.Now:yyyy-MM-dd HH:mm}");
        Line("");
        Line($"Transcription segment vs spec §3 budget ({LatencyBudget.Transcribe.TotalSeconds:F2}s), real WhisperTranscriber, warm.");
        Line($"Fixtures: `{fixturesDir}` · {runs} timed runs/clip (median).");
        Line("");

        var transcriber = await WhisperTranscriber.CreateAsync();
        transcriber.ProcessorRebuilt += reason => Progress($"  ! processor rebuilt ({reason})");
        Line($"Model warm - load {transcriber.ModelLoadTime.TotalMilliseconds:F0} ms, thread cap {Outspoken.Core.CoreInfo.WhisperThreadCap}.");
        Line("");

        try
        {
            // Cold-start reproduction: sit idle so Windows throttles this background process (EcoQoS),
            // then measure the first transcription - the real tray-app scenario the hot loop hides.
            if (idleSeconds > 0)
            {
                var cold10 = Path.Combine(fixturesDir, "clip_10s.wav");
                if (File.Exists(cold10))
                {
                    var coldAudio = LoadWav(cold10);
                    Progress($"idling {idleSeconds}s to let the OS throttle the process…");
                    Thread.Sleep(TimeSpan.FromSeconds(idleSeconds));
                    var coldSw = System.Diagnostics.Stopwatch.StartNew();
                    _ = await transcriber.TranscribeAsync(coldAudio);
                    coldSw.Stop();
                    var coldLine = new BudgetLine("cold transcribe", coldSw.Elapsed, LatencyBudget.Transcribe);
                    Line($"## COLD START (after {idleSeconds}s idle)");
                    Line($"- first transcription (10s clip): **{coldSw.Elapsed.TotalSeconds:F2}s** / {LatencyBudget.Transcribe.TotalSeconds:F2}s budget - **{(coldLine.Over ? "OVER" : "ok")}**");
                    Line("");
                }
            }

            foreach (var clip in Clips)
            {
                var path = Path.Combine(fixturesDir, clip.FileName);
                if (!File.Exists(path))
                {
                    Line($"## {clip.FileName} - MISSING (skipped)");
                    Line("");
                    continue;
                }

                Progress($"[{clip.FileName}] loading…");
                var audio = LoadWav(path);
                Progress($"[{clip.FileName}] loaded {audio.Duration.TotalSeconds:F1}s, peak {audio.Peak:F3}; warm-up…");

                // One warm-up pass (discarded) so the reported runs exclude any first-call cost.
                var warm = System.Diagnostics.Stopwatch.StartNew();
                var text = await transcriber.TranscribeAsync(audio);
                warm.Stop();
                Progress($"[{clip.FileName}] warm-up {warm.Elapsed.TotalSeconds:F2}s");

                var timings = new List<TimeSpan>(runs);
                for (var i = 0; i < runs; i++)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    text = await transcriber.TranscribeAsync(audio);
                    sw.Stop();
                    timings.Add(sw.Elapsed);
                    Progress($"[{clip.FileName}] run {i + 1}/{runs}: {sw.Elapsed.TotalSeconds:F2}s");
                }

                var median = Median(timings);
                var min = timings.Min();
                var max = timings.Max();
                var line = new BudgetLine("transcribe", median, LatencyBudget.Transcribe);

                Line($"## {clip.FileName} ({audio.Duration.TotalSeconds:F1}s audio)");
                Line($"- transcribe: median **{median.TotalSeconds:F2}s** (min {min.TotalSeconds:F2}s, max {max.TotalSeconds:F2}s) / {LatencyBudget.Transcribe.TotalSeconds:F2}s budget - **{(line.Over ? "OVER" : "ok")}** ({line.Ratio:P0})");
                if (clip.ExpectedFile is not null)
                {
                    var expectedPath = Path.Combine(fixturesDir, clip.ExpectedFile);
                    if (File.Exists(expectedPath))
                    {
                        var expected = (await File.ReadAllTextAsync(expectedPath)).Trim();
                        var similarity = TranscriptSimilarity.Compare(expected, text);
                        Line($"- accuracy: **{similarity:P1}** vs expected transcript");
                    }
                }
                Line($"- text: \"{text}\"");
                Line("");
            }
        }
        finally
        {
            transcriber.Dispose();
        }

        try
        {
            await File.WriteAllTextAsync(outputPath, sb.ToString());
            Console.WriteLine($"\nReport written to {outputPath}");
        }
        catch (Exception ex)
        {
            // The measurement already printed to the console; a bad output path shouldn't lose it.
            Console.WriteLine($"\n(could not write report to '{outputPath}': {ex.Message})");
        }
    }

    private static CapturedAudio LoadWav(string path)
    {
        using var reader = new AudioFileReader(path); // 32-bit float samples, interleaved
        var format = reader.WaveFormat;
        var samples = new List<float>((int)(reader.Length / sizeof(float)));
        var buffer = new float[format.SampleRate * format.Channels];
        int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
            for (var i = 0; i < read; i++)
                samples.Add(buffer[i]);

        return AudioConverter.ToWhisperFormat(samples.ToArray(), format.Channels, format.SampleRate);
    }

    private static TimeSpan Median(List<TimeSpan> values)
    {
        var sorted = values.OrderBy(t => t).ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1
            ? sorted[mid]
            : TimeSpan.FromTicks((sorted[mid - 1].Ticks + sorted[mid].Ticks) / 2);
    }

    /// <summary>Walk up from the running exe to the repo and find the committed fixture clips.</summary>
    private static string FindFixturesDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tools", "WhisperBenchmark", "fixtures");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate tools/WhisperBenchmark/fixtures relative to the exe.");
    }
}
