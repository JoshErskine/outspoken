using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Outspoken.Core.Hotkeys;

/// <summary>
/// Hosts a WH_KEYBOARD_LL hook on a dedicated background thread with its own message pump
/// (a low-level hook only fires while its installing thread pumps messages) and feeds every
/// key event through a <see cref="HotkeyStateMachine"/>. RegisterHotKey can't see key-up,
/// which hold-to-talk requires — hence the hook (ADR/plan T3).
///
/// Events are raised on the hook thread; callers marshal to their own thread (the WPF app
/// uses its Dispatcher). Handlers must return fast — Windows silently removes hooks that
/// stall the input chain.
/// </summary>
public sealed class KeyboardHookService : IHotkeySource, IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
    private const int WM_QUIT_LOOP = 0x0012; // WM_QUIT
    private const uint LLKHF_INJECTED = 0x10;
    private const ushort VK_DUMMY = 0xFF;   // undefined VK — inert, but resets the OS's Win-key chord tracking

    private readonly HotkeyStateMachine _stateMachine;
    private readonly Thread _hookThread;
    private readonly ManualResetEventSlim _started = new();
    private nint _hookHandle;
    private uint _hookThreadId;
    private volatile bool _disposed;

    // Keeps the callback delegate alive for the hook's lifetime (GC must not collect it).
    private readonly LowLevelKeyboardProc _callback;

    public KeyboardHookService(HotkeyCombo? combo = null)
    {
        _stateMachine = new HotkeyStateMachine(combo ?? HotkeyCombo.Default, isPhysicallyDown: IsChordKeyPhysicallyDown);
        _stateMachine.HoldStarted += () => HoldStarted?.Invoke();
        _stateMachine.HoldEnded += e =>
        {
            if (_stateMachine.HoldEndedNeedsDummyKey)
                InjectDummyKey();
            HoldEnded?.Invoke(e);
        };

        _callback = HookCallback;
        _hookThread = new Thread(HookThreadMain) { Name = "Outspoken.KeyboardHook", IsBackground = true };
        _hookThread.Start();
        _started.Wait();
        if (_hookHandle == 0)
            throw new Win32Exception("SetWindowsHookEx(WH_KEYBOARD_LL) failed.");
    }

    /// <summary>Raised on the hook thread when the push-to-talk combo completes.</summary>
    public event Action? HoldStarted;

    /// <summary>Raised on the hook thread when any combo key is released.</summary>
    public event Action<HoldEnded>? HoldEnded;

    private void HookThreadMain()
    {
        _hookThreadId = GetCurrentThreadId();
        _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _callback, GetModuleHandle(null), 0);
        _started.Set();
        if (_hookHandle == 0)
            return;

        while (GetMessage(out var msg, 0, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        UnhookWindowsHookEx(_hookHandle);
    }

    private nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0 && !_disposed)
        {
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

            // Ignore events we (or other software) injected — our own dummy key must not recurse.
            if ((data.flags & LLKHF_INJECTED) == 0)
            {
                var isDown = wParam is WM_KEYDOWN or WM_SYSKEYDOWN;
                var isUp = wParam is WM_KEYUP or WM_SYSKEYUP;
                if (isDown || isUp)
                {
                    var decision = _stateMachine.Process((int)data.vkCode, isDown);
                    if (decision.Suppress)
                        return 1; // swallow: the event never reaches the OS or the focused app
                }
            }
        }

        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    /// <summary>
    /// Sends an inert key tap so the OS sees "Win + something" instead of a bare Win press,
    /// which would otherwise open the Start menu when the user releases the combo.
    /// </summary>
    private static void InjectDummyKey()
    {
        var inputs = new INPUT[2];
        inputs[0].type = 1; // INPUT_KEYBOARD
        inputs[0].ki.wVk = VK_DUMMY;
        inputs[1].type = 1;
        inputs[1].ki.wVk = VK_DUMMY;
        inputs[1].ki.dwFlags = 2; // KEYEVENTF_KEYUP
        SendInput(2, inputs, Marshal.SizeOf<INPUT>());
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_hookThreadId != 0)
            PostThreadMessage(_hookThreadId, WM_QUIT_LOOP, 0, 0);
        _hookThread.Join(TimeSpan.FromSeconds(2));
        _started.Dispose();
    }

    private delegate nint LowLevelKeyboardProc(int nCode, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public KEYBDINPUT ki;
        private readonly ulong _padding; // MOUSEINPUT is larger than KEYBDINPUT; keep union size
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public nint hwnd;
        public uint message;
        public nuint wParam;
        public nint lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("kernel32.dll")]
    private static extern nint GetModuleHandle(string? lpModuleName);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint idThread, uint msg, nuint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    // Virtual-key codes for the physical-state check (left/right variants both count).
    private const int VK_CONTROL = 0x11, VK_MENU = 0x12, VK_SHIFT = 0x10, VK_LWIN = 0x5B, VK_RWIN = 0x5C;

    private static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    /// <summary>Ground truth from the OS — is a normalized chord key actually held right now?</summary>
    private static bool IsChordKeyPhysicallyDown(ChordKey key) => key switch
    {
        ChordKey.Ctrl => Down(VK_CONTROL),
        ChordKey.Shift => Down(VK_SHIFT),
        ChordKey.Alt => Down(VK_MENU),
        ChordKey.Win => Down(VK_LWIN) || Down(VK_RWIN),
        _ => false,
    };
}
