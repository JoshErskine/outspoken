namespace Outspoken.Core.Injection;

/// <summary>
/// The OS-facing operations the injection engine orchestrates. Faked in unit tests;
/// implemented over UIA + Win32 clipboard + SendInput in production.
/// </summary>
public interface IInjectionEnvironment
{
    /// <summary>
    /// UIA pre-flight: is the focused element clearly NOT editable? Per ADR-003 the check
    /// biases toward attempting injection — return true (editable) when uncertain.
    /// </summary>
    bool IsFocusedElementEditable();

    /// <summary>Current clipboard text, or null when the clipboard has no text (empty, or an image — sacrificed per ADR-003).</summary>
    string? GetClipboardText();

    /// <summary>Writes text to the clipboard. False on failure.</summary>
    bool TrySetClipboardText(string text);

    /// <summary>
    /// Clears any modifier keys the OS still considers down (the user often still holds
    /// Win/Ctrl from the push-to-talk combo when the paste fires ~1.7s after release —
    /// without this, Ctrl+V arrives as Ctrl+Win+V and collides with OS chords).
    /// </summary>
    void NeutralizeModifiers();

    /// <summary>Sends Ctrl+V to the focused window. False when the send itself failed.</summary>
    bool SendPaste();

    /// <summary>Waits for the target app to consume the paste before the clipboard is restored (~150ms per ADR-003).</summary>
    Task SettleAsync(CancellationToken cancellationToken = default);
}
