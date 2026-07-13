namespace Outspoken.Core.Injection;

public enum InjectionOutcome
{
    /// <summary>Text pasted at the cursor; user's clipboard restored.</summary>
    Injected,

    /// <summary>Text pasted at the cursor, but restoring the user's clipboard failed — dictation text is still on it.</summary>
    InjectedWithoutRestore,

    /// <summary>Injection skipped or failed; the dictation text is on the clipboard (overlay: "Copied — press Ctrl+V").</summary>
    CopiedToClipboard,

    /// <summary>Everything failed, including the clipboard write. Text survives only in <see cref="InjectionResult.Text"/> — caller must surface it.</summary>
    Failed,
}

/// <summary>
/// Always carries the dictated text, whatever happened — the never-lost invariant's
/// last line of defense (spec §4: text ends at the cursor or on the clipboard, never gone).
/// </summary>
public sealed record InjectionResult(InjectionOutcome Outcome, string Text);
