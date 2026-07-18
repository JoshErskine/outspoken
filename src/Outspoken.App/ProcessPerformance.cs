using System.Runtime.InteropServices;

namespace Outspoken.App;

/// <summary>
/// Opts the process out of Windows power throttling (EcoQoS). A push-to-talk tray app sits idle
/// almost all the time, so Windows parks it in efficiency mode - which makes the first Whisper
/// transcription after idle crawl (measured 7s live, &gt;100s in a fully-idle repro, vs ~0.75s when
/// the CPU is already hot). Disabling EXECUTION_SPEED throttling keeps this process at full
/// performance so a dictation is fast the instant the key is released (T12, spec §3 budget).
/// The idle cost is negligible - an idle process consumes ~no CPU whether throttled or not.
/// </summary>
internal static class ProcessPerformance
{
    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_POWER_THROTTLING_STATE
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessInformation(
        IntPtr hProcess, int informationClass, ref PROCESS_POWER_THROTTLING_STATE information, uint length);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    private const int ProcessPowerThrottling = 4; // PROCESS_INFORMATION_CLASS.ProcessPowerThrottling
    private const uint CurrentVersion = 1;         // PROCESS_POWER_THROTTLING_CURRENT_VERSION
    private const uint ExecutionSpeed = 0x1;        // PROCESS_POWER_THROTTLING_EXECUTION_SPEED

    /// <summary>
    /// Disable EcoQoS execution-speed throttling for this process. Best-effort - a failure (older
    /// Windows, denied) just leaves the OS default in place, so it is safe to call unconditionally.
    /// </summary>
    public static bool DisableThrottling()
    {
        var state = new PROCESS_POWER_THROTTLING_STATE
        {
            Version = CurrentVersion,
            ControlMask = ExecutionSpeed, // we are managing execution speed…
            StateMask = 0,                // …and turning throttling OFF (always full speed).
        };
        return SetProcessInformation(GetCurrentProcess(), ProcessPowerThrottling, ref state, (uint)Marshal.SizeOf(state));
    }
}
