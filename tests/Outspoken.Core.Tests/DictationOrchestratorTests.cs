using Outspoken.Core.Audio;
using Outspoken.Core.Cleanup;
using Outspoken.Core.Hotkeys;
using Outspoken.Core.Injection;
using Outspoken.Core.Orchestration;
using Outspoken.Core.Transcription;

namespace Outspoken.Core.Tests;

public class DictationOrchestratorTests
{
    private sealed class FakeHotkeys : IHotkeySource
    {
        public event Action? HoldStarted;
        public event Action<HoldEnded>? HoldEnded;
        public void PressAndHold() => HoldStarted?.Invoke();
        public void Release(bool raw = false) => HoldEnded?.Invoke(new HoldEnded(TimeSpan.FromSeconds(2), raw));
    }

    private sealed class FakeAudio : IAudioCaptureService
    {
        public bool Started;
        public bool StartThrows;
        public float CurrentLevel => 0f;

        public void Start()
        {
            if (StartThrows) throw new InvalidOperationException("no mic");
            Started = true;
        }

        public CapturedAudio Stop()
        {
            Started = false;
            return new CapturedAudio(new float[16_000], CapturedAudio.WhisperSampleRate);
        }
    }

    private sealed class FakeTranscriber : ITranscriber
    {
        public string Result = "hello world";
        public bool Throws;

        public Task<string> TranscribeAsync(CapturedAudio audio, CancellationToken ct = default)
        {
            if (Throws) throw new InvalidOperationException("model exploded");
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeInjector : IInjector
    {
        public List<string> Injected { get; } = [];

        public Task<InjectionResult> InjectAsync(string text, CancellationToken ct = default)
        {
            Injected.Add(text);
            return Task.FromResult(new InjectionResult(InjectionOutcome.Injected, text));
        }
    }

    private sealed class FakeCleanup : ICleanupClient
    {
        public bool Called;
        public string? SawRaw;
        public Func<string, CleanupResult> Behavior = raw => CleanupResult.Cleaned($"[cleaned] {raw}");

        public Task<CleanupResult> CleanAsync(string rawTranscript, CancellationToken ct = default)
        {
            Called = true;
            SawRaw = rawTranscript;
            return Task.FromResult(Behavior(rawTranscript));
        }
    }

    private static (FakeHotkeys, FakeAudio, FakeTranscriber, FakeInjector, FakeCleanup, DictationOrchestrator) Create()
    {
        var hotkeys = new FakeHotkeys();
        var audio = new FakeAudio();
        var transcriber = new FakeTranscriber();
        var injector = new FakeInjector();
        var cleanup = new FakeCleanup();
        var orchestrator = new DictationOrchestrator(hotkeys, audio, transcriber, injector, cleanup);
        return (hotkeys, audio, transcriber, injector, cleanup, orchestrator);
    }

    private static async Task<T> WaitFor<T>(TaskCompletionSource<T> tcs) =>
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

    [Fact]
    public async Task FullDictation_TranscribesAndInjects_ReportsTimings()
    {
        var (hotkeys, audio, _, injector, cleanup, orch) = Create();
        var done = new TaskCompletionSource<DictationReport>();
        orch.Completed += r => done.TrySetResult(r);

        hotkeys.PressAndHold();
        Assert.True(audio.Started);
        Assert.Equal(DictationState.Listening, orch.State);

        hotkeys.Release();
        var report = await WaitFor(done);

        // Cleanup ran and its output is what gets injected.
        Assert.True(cleanup.Called);
        Assert.Equal("hello world", cleanup.SawRaw);
        Assert.True(report.WasCleaned);
        Assert.Equal("[cleaned] hello world", report.Text);
        Assert.Equal(InjectionOutcome.Injected, report.Outcome);
        Assert.Equal(["[cleaned] hello world"], injector.Injected);
        Assert.Equal(TimeSpan.FromSeconds(1), report.AudioDuration);
        Assert.True(report.TotalFromRelease >= report.TranscribeTime);
        Assert.Equal(DictationState.Idle, orch.State);
    }

    [Fact]
    public async Task RawMode_SkipsCleanup_InjectsRawTranscript()
    {
        var (hotkeys, _, _, injector, cleanup, orch) = Create();
        var done = new TaskCompletionSource<DictationReport>();
        orch.Completed += r => done.TrySetResult(r);

        hotkeys.PressAndHold();
        hotkeys.Release(raw: true);
        var report = await WaitFor(done);

        Assert.True(report.RawMode);
        Assert.False(report.WasCleaned);
        Assert.False(cleanup.Called);               // Shift-held: cleanup never invoked
        Assert.Equal(["hello world"], injector.Injected); // raw transcript, uncleaned
    }

    [Fact]
    public async Task CleanupFailure_FallsBackToRaw_StillInjects()
    {
        var (hotkeys, _, _, injector, cleanup, orch) = Create();
        cleanup.Behavior = raw => CleanupResult.Raw(raw, "cleanup timed out");
        var done = new TaskCompletionSource<DictationReport>();
        orch.Completed += r => done.TrySetResult(r);

        hotkeys.PressAndHold();
        hotkeys.Release();
        var report = await WaitFor(done);

        Assert.False(report.WasCleaned);
        Assert.Equal("cleanup timed out", report.CleanupFallbackReason);
        Assert.Equal(["hello world"], injector.Injected); // raw survives — never-block invariant
    }

    [Fact]
    public async Task NoCleanupClient_DeliversRaw()
    {
        var hotkeys = new FakeHotkeys();
        var audio = new FakeAudio();
        var injector = new FakeInjector();
        using var orch = new DictationOrchestrator(hotkeys, audio, new FakeTranscriber(), injector, cleanup: null);
        var done = new TaskCompletionSource<DictationReport>();
        orch.Completed += r => done.TrySetResult(r);

        hotkeys.PressAndHold();
        hotkeys.Release();
        var report = await WaitFor(done);

        Assert.False(report.WasCleaned);
        Assert.Equal(["hello world"], injector.Injected);
    }

    [Fact]
    public async Task Silence_RaisesFailed_NothingInjected()
    {
        var (hotkeys, _, transcriber, injector, _, orch) = Create();
        transcriber.Result = "";
        var failed = new TaskCompletionSource<string>();
        orch.Failed += m => failed.TrySetResult(m);

        hotkeys.PressAndHold();
        hotkeys.Release();

        Assert.Contains("silence", await WaitFor(failed));
        Assert.Empty(injector.Injected);
        Assert.Equal(DictationState.Idle, orch.State);
    }

    [Fact]
    public async Task TranscriberFailure_RaisesFailed_ReturnsToIdle()
    {
        var (hotkeys, _, transcriber, injector, _, orch) = Create();
        transcriber.Throws = true;
        var failed = new TaskCompletionSource<string>();
        orch.Failed += m => failed.TrySetResult(m);

        hotkeys.PressAndHold();
        hotkeys.Release();

        Assert.Contains("model exploded", await WaitFor(failed));
        Assert.Empty(injector.Injected);
        Assert.Equal(DictationState.Idle, orch.State);
    }

    [Fact]
    public void MicFailure_RaisesFailed_StaysIdle()
    {
        var (hotkeys, audio, _, _, _, orch) = Create();
        audio.StartThrows = true;
        string? failure = null;
        orch.Failed += m => failure = m;

        hotkeys.PressAndHold();

        Assert.NotNull(failure);
        Assert.Equal(DictationState.Idle, orch.State);
    }

    [Fact]
    public async Task StateSequence_IsListeningProcessingIdle()
    {
        var (hotkeys, _, _, _, _, orch) = Create();
        var states = new List<DictationState>();
        var done = new TaskCompletionSource<DictationReport>();
        orch.StateChanged += states.Add;
        orch.Completed += r => done.TrySetResult(r);

        hotkeys.PressAndHold();
        hotkeys.Release();
        await WaitFor(done);

        Assert.Equal([DictationState.Listening, DictationState.Processing, DictationState.Idle], states);
    }

    [Fact]
    public async Task Dispose_UnsubscribesFromHotkeys()
    {
        var (hotkeys, audio, _, _, _, orch) = Create();
        orch.Dispose();

        hotkeys.PressAndHold();
        await Task.Delay(50);

        Assert.False(audio.Started);
        Assert.Equal(DictationState.Idle, orch.State);
    }
}
