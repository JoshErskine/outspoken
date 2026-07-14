using Outspoken.Core.Audio;

// Plays the real start cue through AudioCuePlayer (WASAPI + PCM16 path), reference held.
Console.WriteLine("Playing start cue via AudioCuePlayer…");
using var player = new AudioCuePlayer();
player.PlayStart();
await Task.Delay(400);
player.PlayStop();
await Task.Delay(600);
Console.WriteLine("done");
