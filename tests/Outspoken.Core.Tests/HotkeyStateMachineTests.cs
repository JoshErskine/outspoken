using Outspoken.Core.Hotkeys;

namespace Outspoken.Core.Tests;

public class HotkeyStateMachineTests
{
    private const int VK_LCONTROL = 0xA2;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int VK_LSHIFT = 0xA0;
    private const int VK_A = 0x41;

    private static HotkeyStateMachine Create() => new(HotkeyCombo.Default);

    [Fact]
    public void FullChord_StartsHold_AndCompletingDownIsSuppressed()
    {
        var sm = Create();
        var started = false;
        sm.HoldStarted += () => started = true;

        var first = sm.Process(VK_LCONTROL, isDown: true);
        Assert.False(first.Suppress); // partial chord passes through (normal Ctrl use)
        Assert.False(started);

        var completing = sm.Process(VK_LWIN, isDown: true);
        Assert.True(completing.Suppress);
        Assert.True(started);
        Assert.True(sm.IsHolding);
    }

    [Fact]
    public void ReleasingAnyComboKey_EndsHold_UpNotSuppressed()
    {
        var sm = Create();
        HoldEnded? ended = null;
        sm.HoldEnded += e => ended = e;

        sm.Process(VK_LCONTROL, true);
        sm.Process(VK_LWIN, true);
        var up = sm.Process(VK_LCONTROL, false);

        Assert.False(up.Suppress);
        Assert.NotNull(ended);
        Assert.False(ended.RawMode);
        Assert.False(sm.IsHolding);
    }

    [Fact]
    public void SingleComboKey_DoesNotTrigger()
    {
        var sm = Create();
        var started = false;
        sm.HoldStarted += () => started = true;

        sm.Process(VK_LWIN, true);
        sm.Process(VK_LWIN, false);

        Assert.False(started);
    }

    [Fact]
    public void OrdinaryKeys_PassThrough_WhenNotHolding()
    {
        var sm = Create();
        Assert.False(sm.Process(VK_A, true).Suppress);
        Assert.False(sm.Process(VK_A, false).Suppress);
    }

    [Fact]
    public void KeysDuringHold_AreSuppressed()
    {
        var sm = Create();
        sm.Process(VK_LCONTROL, true);
        sm.Process(VK_LWIN, true);

        Assert.True(sm.Process(VK_A, true).Suppress);        // no leak into focused app
        Assert.True(sm.Process(VK_LWIN, true).Suppress);      // key-repeat of chord key
        Assert.True(sm.Process(VK_LSHIFT, true).Suppress);    // raw modifier mid-hold
    }

    [Fact]
    public void ShiftHeldAtChordCompletion_SetsRawMode()
    {
        var sm = Create();
        HoldEnded? ended = null;
        sm.HoldEnded += e => ended = e;

        sm.Process(VK_LSHIFT, true);
        sm.Process(VK_LCONTROL, true);
        sm.Process(VK_LWIN, true);
        sm.Process(VK_LWIN, false);

        Assert.True(ended!.RawMode);
    }

    [Fact]
    public void ShiftPressedMidHold_SetsRawMode()
    {
        var sm = Create();
        HoldEnded? ended = null;
        sm.HoldEnded += e => ended = e;

        sm.Process(VK_LCONTROL, true);
        sm.Process(VK_LWIN, true);
        sm.Process(VK_LSHIFT, true);
        sm.Process(VK_LWIN, false);

        Assert.True(ended!.RawMode);
    }

    [Fact]
    public void LeftAndRightVariants_AreEquivalent()
    {
        var sm = Create();
        var started = false;
        sm.HoldStarted += () => started = true;

        sm.Process(VK_LCONTROL, true);
        sm.Process(VK_RWIN, true); // right Win completes the chord too

        Assert.True(started);
    }

    [Fact]
    public void WinCombo_RequiresDummyKeyOnHoldEnd()
    {
        Assert.True(Create().HoldEndedNeedsDummyKey);
    }

    [Fact]
    public void HoldCanRestart_AfterRelease()
    {
        var sm = Create();
        var starts = 0;
        sm.HoldStarted += () => starts++;

        sm.Process(VK_LCONTROL, true);
        sm.Process(VK_LWIN, true);
        sm.Process(VK_LWIN, false);
        sm.Process(VK_LWIN, true); // Ctrl still down: chord completes again

        Assert.Equal(2, starts);
    }
}
