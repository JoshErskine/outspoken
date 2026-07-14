namespace Outspoken.Core.Hotkeys;

public sealed record HoldEnded(TimeSpan Duration, bool RawMode);

/// <summary>What the hook should do with a key event after the state machine has seen it.</summary>
public readonly record struct KeyDecision(bool Suppress);

/// <summary>
/// Pure hold-to-talk logic, driven by a stream of key down/up events from the low-level hook.
/// Owns no Win32 state, so it is fully unit-testable.
///
/// Semantics:
/// - Hold starts when every combo key is down; it ends when any of them goes up.
/// - The key-down that completes the combo is suppressed (the OS never sees the chord form,
///   so Win-key OS shortcuts can't fire). Earlier partial-chord downs pass through: alone
///   they are inert modifiers, and suppressing them would break normal typing (Ctrl+C etc.).
/// - While holding, combo-key repeats and all other key-downs are suppressed so nothing
///   leaks into the focused app mid-dictation.
/// - Key-ups are never suppressed: the OS saw (some of) the downs, and eating the matching
///   ups would leave it thinking a modifier is stuck down.
/// - If the combo includes Win, the hook must inject a dummy key event when the hold ends
///   (see <see cref="HoldEndedNeedsDummyKey"/>): the OS saw Win go down and up with nothing
///   in between and would otherwise open the Start menu.
/// - Raw mode: true if the raw modifier is down at any point during the hold.
/// </summary>
public sealed class HotkeyStateMachine
{
    private readonly HotkeyCombo _combo;
    private readonly TimeProvider _time;
    private readonly Func<ChordKey, bool> _isPhysicallyDown;
    private readonly HashSet<ChordKey> _downChordKeys = [];

    private bool _holding;
    private bool _rawSeen;
    private long _holdStartTimestamp;

    /// <param name="isPhysicallyDown">
    /// Ground-truth physical key state (GetAsyncKeyState in production). A low-level hook can
    /// miss a keyup (heavy load, focus change), leaving a key stale in the tracked set — which
    /// would let a lone combo key falsely complete the chord. Verifying physical state at
    /// completion makes that impossible. Defaults to "trust the tracked set" for tests.
    /// </param>
    public HotkeyStateMachine(HotkeyCombo combo, TimeProvider? time = null, Func<ChordKey, bool>? isPhysicallyDown = null)
    {
        _combo = combo;
        _time = time ?? TimeProvider.System;
        _isPhysicallyDown = isPhysicallyDown ?? (_ => true);
    }

    public event Action? HoldStarted;
    public event Action<HoldEnded>? HoldEnded;

    /// <summary>True when the ended hold needs a dummy key injected before the modifier ups reach the OS.</summary>
    public bool HoldEndedNeedsDummyKey => _combo.Keys.Contains(ChordKey.Win);

    public bool IsHolding => _holding;

    public KeyDecision Process(int virtualKey, bool isDown)
    {
        var chord = HotkeyCombo.Normalize(virtualKey);

        if (isDown)
            return ProcessDown(chord);
        return ProcessUp(chord);
    }

    private KeyDecision ProcessDown(ChordKey? chord)
    {
        if (chord is { } key)
        {
            var isComboKey = _combo.Keys.Contains(key);
            var newlyDown = _downChordKeys.Add(key);

            if (_holding)
            {
                if (key == _combo.RawModeModifier)
                {
                    _rawSeen = true;
                    return new KeyDecision(Suppress: true);
                }
                // Combo-key repeats and any other modifier: eat them while dictating.
                return new KeyDecision(Suppress: true);
            }

            // The key that just fired this down-event is trusted (its own physical state may not
            // be committed yet — checking GetAsyncKeyState on it here can spuriously read "up").
            // Every OTHER combo key must be both tracked AND physically down, which is what
            // rejects a stale key from a missed keyup.
            if (isComboKey && newlyDown &&
                _combo.Keys.All(k => _downChordKeys.Contains(k) && (k == key || _isPhysicallyDown(k))))
            {
                _holding = true;
                _rawSeen = _combo.RawModeModifier is { } raw && _downChordKeys.Contains(raw);
                _holdStartTimestamp = _time.GetTimestamp();
                HoldStarted?.Invoke();
                return new KeyDecision(Suppress: true); // the chord-completing down
            }

            return new KeyDecision(Suppress: false);
        }

        // Ordinary (non-modifier) key.
        if (_holding)
            return new KeyDecision(Suppress: true); // nothing leaks into the focused app mid-hold

        return new KeyDecision(Suppress: false);
    }

    private KeyDecision ProcessUp(ChordKey? chord)
    {
        if (chord is { } key)
        {
            _downChordKeys.Remove(key);

            if (_holding && _combo.Keys.Contains(key))
            {
                _holding = false;
                var duration = _time.GetElapsedTime(_holdStartTimestamp);
                HoldEnded?.Invoke(new HoldEnded(duration, _rawSeen));
            }
        }

        return new KeyDecision(Suppress: false); // ups always pass through (no stuck modifiers)
    }
}
