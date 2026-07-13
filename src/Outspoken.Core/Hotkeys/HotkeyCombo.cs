namespace Outspoken.Core.Hotkeys;

/// <summary>
/// Chord keys are normalized: left/right variants of a modifier map to one chord key,
/// so "Ctrl+Win" means any Ctrl plus any Win key.
/// </summary>
public enum ChordKey
{
    Ctrl,
    Win,
    Shift,
    Alt,
}

public sealed class HotkeyCombo
{
    /// <summary>Default push-to-talk combo (spec: Ctrl+Win, raw mode adds Shift).</summary>
    public static HotkeyCombo Default { get; } = new([ChordKey.Ctrl, ChordKey.Win], ChordKey.Shift);

    public HotkeyCombo(IReadOnlyCollection<ChordKey> keys, ChordKey? rawModeModifier)
    {
        if (keys.Count == 0)
            throw new ArgumentException("Combo needs at least one key.", nameof(keys));
        if (rawModeModifier is { } raw && keys.Contains(raw))
            throw new ArgumentException("Raw-mode modifier cannot also be a combo key.", nameof(rawModeModifier));

        Keys = keys.ToHashSet();
        RawModeModifier = rawModeModifier;
    }

    public IReadOnlySet<ChordKey> Keys { get; }

    /// <summary>Held together with the combo to request raw (no-cleanup) dictation. Null disables raw mode.</summary>
    public ChordKey? RawModeModifier { get; }

    /// <summary>Maps a Win32 virtual-key code to its normalized chord key, or null for ordinary keys.</summary>
    public static ChordKey? Normalize(int virtualKey) => virtualKey switch
    {
        0x11 or 0xA2 or 0xA3 => ChordKey.Ctrl,  // VK_CONTROL, VK_LCONTROL, VK_RCONTROL
        0x5B or 0x5C => ChordKey.Win,           // VK_LWIN, VK_RWIN
        0x10 or 0xA0 or 0xA1 => ChordKey.Shift, // VK_SHIFT, VK_LSHIFT, VK_RSHIFT
        0x12 or 0xA4 or 0xA5 => ChordKey.Alt,   // VK_MENU, VK_LMENU, VK_RMENU
        _ => null,
    };
}
