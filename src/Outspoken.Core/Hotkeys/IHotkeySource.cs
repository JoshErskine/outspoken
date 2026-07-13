namespace Outspoken.Core.Hotkeys;

/// <summary>Push-to-talk event source. Production: <see cref="KeyboardHookService"/>; faked in orchestrator tests.</summary>
public interface IHotkeySource
{
    /// <summary>The combo completed — start listening. May be raised on any thread.</summary>
    event Action? HoldStarted;

    /// <summary>A combo key was released — stop and process. May be raised on any thread.</summary>
    event Action<HoldEnded>? HoldEnded;
}
