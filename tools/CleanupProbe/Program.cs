using System.Diagnostics;
using Outspoken.Core.Cleanup;

// Measures real Haiku cleanup latency on this machine/network, using the DPAPI-stored key.
// Runs 4 sequential calls with a generous timeout so we can see cold-start vs steady-state.

var key = ApiKeyStore.TryLoad();
if (key is null)
{
    Console.WriteLine("No API key stored. Run set-key first.");
    return;
}

var client = new AnthropicCleanupClient(key, timeout: TimeSpan.FromSeconds(30));
var samples = new[]
{
    "um so I want to, uh, sell my no wait buy my shares on Tuesday actually Wednesday",
    "hey there this is a quick test of the cleanup pass",
    "I need to open cell B4 and then sell the other item",
    "so basically the thing is like we should ship this today you know",
};

for (var i = 0; i < samples.Length; i++)
{
    var sw = Stopwatch.StartNew();
    var result = await client.CleanAsync(samples[i]);
    sw.Stop();
    var label = i == 0 ? "call 1 (cold)" : $"call {i + 1}";
    Console.WriteLine($"{label}: {sw.Elapsed.TotalSeconds:F2}s | cleaned={result.WasCleaned}");
    if (result.WasCleaned)
        Console.WriteLine($"   -> {result.Text}");
    else
        Console.WriteLine($"   -> fallback: {result.FallbackReason}");
}
