using Outspoken.Core.Injection;

namespace Outspoken.Core.Tests;

public class InjectionEngineTests
{
    private sealed class FakeEnvironment : IInjectionEnvironment
    {
        public bool Editable = true;
        public bool EditableThrows;
        public string? Clipboard = "user's original clipboard";
        public bool SetClipboardFails;
        public bool SetClipboardFailsOnRestoreOnly;
        public bool PasteSucceeds = true;
        public bool PasteThrows;

        public List<string> Log { get; } = [];
        private int _setCalls;

        public bool IsFocusedElementEditable()
        {
            if (EditableThrows) throw new InvalidOperationException("UIA broke");
            Log.Add("preflight");
            return Editable;
        }

        public string? GetClipboardText()
        {
            Log.Add("get");
            return Clipboard;
        }

        public bool TrySetClipboardText(string text)
        {
            _setCalls++;
            if (SetClipboardFails) return false;
            if (SetClipboardFailsOnRestoreOnly && _setCalls > 1) return false;
            Log.Add($"set:{text}");
            Clipboard = text;
            return true;
        }

        public bool SendPaste()
        {
            if (PasteThrows) throw new InvalidOperationException("SendInput broke");
            Log.Add("paste");
            return PasteSucceeds;
        }

        public Task SettleAsync(CancellationToken ct = default)
        {
            Log.Add("settle");
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task HappyPath_PastesAndRestoresClipboard()
    {
        var env = new FakeEnvironment();
        var result = await new InjectionEngine(env).InjectAsync("dictated text");

        Assert.Equal(InjectionOutcome.Injected, result.Outcome);
        Assert.Equal(["preflight", "get", "set:dictated text", "paste", "settle", "set:user's original clipboard"], env.Log);
        Assert.Equal("user's original clipboard", env.Clipboard); // restored
    }

    [Fact]
    public async Task NotEditable_CopiesToClipboard_NeverPastes()
    {
        var env = new FakeEnvironment { Editable = false };
        var result = await new InjectionEngine(env).InjectAsync("dictated text");

        Assert.Equal(InjectionOutcome.CopiedToClipboard, result.Outcome);
        Assert.DoesNotContain("paste", env.Log);
        Assert.Equal("dictated text", env.Clipboard); // text on clipboard, original sacrificed
    }

    [Fact]
    public async Task PasteFails_TextStaysOnClipboard_NoRestore()
    {
        var env = new FakeEnvironment { PasteSucceeds = false };
        var result = await new InjectionEngine(env).InjectAsync("dictated text");

        Assert.Equal(InjectionOutcome.CopiedToClipboard, result.Outcome);
        Assert.Equal("dictated text", env.Clipboard); // NOT restored over the dictation
        Assert.DoesNotContain("settle", env.Log);
    }

    [Fact]
    public async Task PasteThrows_TreatedAsPasteFailure()
    {
        var env = new FakeEnvironment { PasteThrows = true };
        var result = await new InjectionEngine(env).InjectAsync("dictated text");

        Assert.Equal(InjectionOutcome.CopiedToClipboard, result.Outcome);
        Assert.Equal("dictated text", env.Clipboard);
    }

    [Fact]
    public async Task PreflightThrows_AttemptsInjectionAnyway()
    {
        var env = new FakeEnvironment { EditableThrows = true };
        var result = await new InjectionEngine(env).InjectAsync("dictated text");

        Assert.Equal(InjectionOutcome.Injected, result.Outcome);
        Assert.Contains("paste", env.Log);
    }

    [Fact]
    public async Task RestoreFails_ReportsInjectedWithoutRestore()
    {
        var env = new FakeEnvironment { SetClipboardFailsOnRestoreOnly = true };
        var result = await new InjectionEngine(env).InjectAsync("dictated text");

        Assert.Equal(InjectionOutcome.InjectedWithoutRestore, result.Outcome);
    }

    [Fact]
    public async Task EmptyOriginalClipboard_NothingRestored_DictationStaysOnClipboard()
    {
        var env = new FakeEnvironment { Clipboard = null };
        var result = await new InjectionEngine(env).InjectAsync("dictated text");

        Assert.Equal(InjectionOutcome.Injected, result.Outcome);
        Assert.Equal("dictated text", env.Clipboard); // no restore attempted over it
    }

    [Fact]
    public async Task ClipboardWriteFails_OnEditablePath_ReportsFailedWithTextInResult()
    {
        var env = new FakeEnvironment { SetClipboardFails = true };
        var result = await new InjectionEngine(env).InjectAsync("dictated text");

        Assert.Equal(InjectionOutcome.Failed, result.Outcome);
        Assert.Equal("dictated text", result.Text); // last line of defense: text lives in the result
        Assert.DoesNotContain("paste", env.Log);
    }

    [Fact]
    public async Task ClipboardWriteFails_OnFallbackPath_ReportsFailedWithTextInResult()
    {
        var env = new FakeEnvironment { Editable = false, SetClipboardFails = true };
        var result = await new InjectionEngine(env).InjectAsync("dictated text");

        Assert.Equal(InjectionOutcome.Failed, result.Outcome);
        Assert.Equal("dictated text", result.Text);
    }

    [Fact]
    public async Task EmptyText_DoesNothing()
    {
        var env = new FakeEnvironment();
        var result = await new InjectionEngine(env).InjectAsync("");

        Assert.Equal(InjectionOutcome.Injected, result.Outcome);
        Assert.Empty(env.Log);
    }
}
