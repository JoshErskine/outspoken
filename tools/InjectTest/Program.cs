using Outspoken.Core.Injection;

// Repro harness for the ",,"-prefix report: injects a known string into whatever
// window has focus after a countdown, bypassing mic/whisper entirely — isolates
// the injection path. Usage: dotnet run -- [text]

var text = args.Length > 0 ? string.Join(' ', args) : "This is an injection test.";
Console.WriteLine($"Focus the target window now — injecting in 4s: \"{text}\"");
await Task.Delay(4000);

var engine = new InjectionEngine(new Win32InjectionEnvironment());
var result = await engine.InjectAsync(text);
Console.WriteLine($"outcome: {result.Outcome}");
