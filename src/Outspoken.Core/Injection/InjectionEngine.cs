namespace Outspoken.Core.Injection;

/// <summary>
/// ADR-003 clipboard-swap injection, pure orchestration over <see cref="IInjectionEnvironment"/>.
///
/// Sequence: UIA pre-flight → save clipboard → set dictation text → Ctrl+V → settle →
/// restore clipboard. The user's clipboard is restored ONLY after a verified paste;
/// on any failure or uncertainty the dictation text stays on the clipboard instead
/// (never-lost invariant, spec §4).
/// </summary>
public sealed class InjectionEngine : IInjector
{
    private readonly IInjectionEnvironment _env;

    public InjectionEngine(IInjectionEnvironment env) => _env = env;

    public async Task<InjectionResult> InjectAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(text))
            return new InjectionResult(InjectionOutcome.Injected, text); // nothing to deliver

        bool editable;
        try
        {
            editable = _env.IsFocusedElementEditable();
        }
        catch
        {
            editable = true; // pre-flight is a heuristic; when it breaks, attempt injection (ADR-003)
        }

        if (!editable)
        {
            // Clearly not editable (desktop, read-only field, elevated window): straight to clipboard.
            return _env.TrySetClipboardText(text)
                ? new InjectionResult(InjectionOutcome.CopiedToClipboard, text)
                : new InjectionResult(InjectionOutcome.Failed, text);
        }

        var savedClipboard = SafeGetClipboard();

        if (!_env.TrySetClipboardText(text))
            return new InjectionResult(InjectionOutcome.Failed, text);

        bool pasted;
        try
        {
            pasted = _env.SendPaste();
        }
        catch
        {
            pasted = false;
        }

        if (!pasted)
        {
            // Paste never went out — dictation text is already on the clipboard; do NOT restore over it.
            return new InjectionResult(InjectionOutcome.CopiedToClipboard, text);
        }

        await _env.SettleAsync(cancellationToken);

        // Verified success — now, and only now, the user's clipboard goes back.
        // A textless original clipboard (empty, or an image) is sacrificed per ADR-003;
        // the dictation text stays on the clipboard, which is the safer failure direction.
        if (savedClipboard is not null && !_env.TrySetClipboardText(savedClipboard))
            return new InjectionResult(InjectionOutcome.InjectedWithoutRestore, text);

        return new InjectionResult(InjectionOutcome.Injected, text);
    }

    private string? SafeGetClipboard()
    {
        try
        {
            return _env.GetClipboardText();
        }
        catch
        {
            return null;
        }
    }
}
