using NAudio.Wave;

// Analyzes a reference cue sound so we can reproduce its character in ToneGenerator.
// Usage: dotnet run -c Release -- <path-to-audio-file>
// Reports: duration, dominant pitch (per third, to catch a glide), and the amplitude
// envelope (attack time + how fast it decays) — the knobs the synthesizer exposes.

if (args.Length == 0)
{
    Console.WriteLine("Usage: dotnet run -c Release -- <audio-file.wav/.mp3/.m4a>");
    return;
}

var path = args[0];
using var reader = new AudioFileReader(path);
var rate = reader.WaveFormat.SampleRate;
var channels = reader.WaveFormat.Channels;

// Read all samples, mixed to mono.
var all = new List<float>();
var buf = new float[rate * channels];
int read;
while ((read = reader.Read(buf, 0, buf.Length)) > 0)
    for (var i = 0; i < read; i += channels)
    {
        var sum = 0f;
        for (var c = 0; c < channels; c++) sum += buf[i + c];
        all.Add(sum / channels);
    }

var full = all.ToArray();
var fullPeak = full.Length == 0 ? 0 : full.Max(Math.Abs);

// Trim to the active region: first/last samples above 3% of peak (ignore silent padding).
var gate = fullPeak * 0.03f;
int lo = 0, hi = full.Length - 1;
while (lo < full.Length && Math.Abs(full[lo]) < gate) lo++;
while (hi > lo && Math.Abs(full[hi]) < gate) hi--;
var samples = full[lo..(hi + 1)];
var seconds = samples.Length / (double)rate;
var peak = samples.Length == 0 ? 0 : samples.Max(Math.Abs);

Console.WriteLine($"file:      {Path.GetFileName(path)}");
Console.WriteLine($"full file: {full.Length / (double)rate * 1000:F0} ms   (active click: {seconds * 1000:F0} ms)");
Console.WriteLine($"peak amp:  {peak:F3}");

// Export mode: trim to the active click + short fades, write a clean 16-bit mono WAV.
if (args.Length >= 2)
{
    var outPath = args[1];
    var gain = args.Length >= 3 ? float.Parse(args[2]) : 1f; // optional loudness multiplier
    var faded = (float[])samples.Clone();
    var fade = Math.Min(rate / 400, faded.Length / 4); // ~2.5ms fade in/out, no click
    for (var i = 0; i < faded.Length; i++)
        faded[i] = Math.Clamp(faded[i] * gain, -1f, 1f);
    for (var i = 0; i < fade; i++)
    {
        var g = i / (double)fade;
        faded[i] *= (float)g;
        faded[^(i + 1)] *= (float)g;
    }
    WriteWav(outPath, faded, rate);
    Console.WriteLine($"exported: {outPath} ({faded.Length / (double)rate * 1000:F0} ms, 16-bit mono)");
    return;
}

// Dominant pitch per third (a downward glide shows as falling Hz).
Console.WriteLine("pitch (dominant Hz per third — a fall = a downward glide):");
for (var t = 0; t < 3; t++)
{
    var start = samples.Length * t / 3;
    var end = samples.Length * (t + 1) / 3;
    Console.WriteLine($"  {t * 33,3}–{(t + 1) * 33,3}%: {DominantHz(samples, start, end, rate):F0} Hz");
}

// Envelope: time-to-peak (attack) and time to fall to 10% of peak after the peak (decay feel).
var peakIdx = ArgMaxAbs(samples);
Console.WriteLine($"attack:    {peakIdx / (double)rate * 1000:F0} ms to peak");
var decayIdx = peakIdx;
while (decayIdx < samples.Length && Math.Abs(samples[decayIdx]) > peak * 0.1) decayIdx++;
Console.WriteLine($"decay:     {(decayIdx - peakIdx) / (double)rate * 1000:F0} ms from peak to 10%");
Console.WriteLine("\nMatch in ToneGenerator: set StartFrom/StartTo ≈ first→last pitch, DurationSeconds ≈ duration, DecayTau ≈ decay/2.3, Amplitude ≈ peak.");

// --- helpers ---
static void WriteWav(string path, float[] samples, int rate)
{
    var pcm = new byte[samples.Length * 2];
    for (var i = 0; i < samples.Length; i++)
    {
        var s = (short)(Math.Clamp(samples[i], -1f, 1f) * short.MaxValue);
        pcm[i * 2] = (byte)(s & 0xFF);
        pcm[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
    }
    using var fs = File.Create(path);
    using var w = new BinaryWriter(fs);
    w.Write("RIFF"u8.ToArray()); w.Write(36 + pcm.Length); w.Write("WAVE"u8.ToArray());
    w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)1);
    w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
    w.Write("data"u8.ToArray()); w.Write(pcm.Length); w.Write(pcm);
}

static int ArgMaxAbs(float[] s)
{
    var idx = 0; var max = 0f;
    for (var i = 0; i < s.Length; i++) { var a = Math.Abs(s[i]); if (a > max) { max = a; idx = i; } }
    return idx;
}

// Cheap dominant-frequency estimate via autocorrelation over 60–1200 Hz.
static double DominantHz(float[] s, int start, int end, int rate)
{
    var minLag = rate / 1200;
    var maxLag = rate / 60;
    var bestLag = minLag; var best = double.NegativeInfinity;
    for (var lag = minLag; lag <= maxLag && start + lag < end; lag++)
    {
        var sum = 0.0;
        for (var i = start; i < end - lag; i++) sum += s[i] * s[i + lag];
        if (sum > best) { best = sum; bestLag = lag; }
    }
    return (double)rate / bestLag;
}
