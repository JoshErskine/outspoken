using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace Outspoken.Core.Injection;

/// <summary>
/// Production <see cref="IInjectionEnvironment"/>: UIA focus pre-flight, Win32 clipboard
/// (text only, per ADR-003), SendInput Ctrl+V. Clipboard access retries briefly because
/// another process can hold the clipboard lock at any moment.
/// </summary>
public sealed class Win32InjectionEnvironment : IInjectionEnvironment
{
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(150);
    private const int ClipboardRetries = 5;
    private const int ClipboardRetryDelayMs = 20;

    public bool IsFocusedElementEditable()
    {
        // Bias toward attempting injection: only a confident "not editable" returns false (ADR-003).
        var focused = AutomationElement.FocusedElement;
        if (focused is null)
            return true;

        if (focused.TryGetCurrentPattern(ValuePattern.Pattern, out var valueObj))
            return !((ValuePattern)valueObj).Current.IsReadOnly;

        if (focused.TryGetCurrentPattern(TextPattern.Pattern, out _))
            return true; // text pattern present; read-only isn't exposed here, so attempt

        // No text-ish pattern at all. Buttons/desktop/list items land here — but so do some
        // terminals and Electron apps with weak UIA support, so only clearly inert control
        // types get the clipboard-only path.
        var controlType = focused.Current.ControlType;
        var clearlyNotEditable =
            controlType == ControlType.Button || controlType == ControlType.ListItem ||
            controlType == ControlType.Image || controlType == ControlType.MenuItem ||
            controlType == ControlType.TreeItem || controlType == ControlType.Hyperlink;
        return !clearlyNotEditable;
    }

    public string? GetClipboardText()
    {
        return WithClipboard(() =>
        {
            var handle = GetClipboardData(CF_UNICODETEXT);
            if (handle == 0)
                return null;
            var ptr = GlobalLock(handle);
            if (ptr == 0)
                return null;
            try
            {
                return Marshal.PtrToStringUni(ptr);
            }
            finally
            {
                GlobalUnlock(handle);
            }
        });
    }

    public bool TrySetClipboardText(string text)
    {
        var ok = WithClipboard(() =>
        {
            EmptyClipboard();
            var bytes = (text.Length + 1) * 2;
            var hGlobal = GlobalAlloc(GMEM_MOVEABLE, (nuint)bytes);
            if (hGlobal == 0)
                return false;
            var target = GlobalLock(hGlobal);
            if (target == 0)
            {
                GlobalFree(hGlobal);
                return false;
            }
            Marshal.Copy(text.ToCharArray(), 0, target, text.Length);
            Marshal.WriteInt16(target, text.Length * 2, 0); // null terminator
            GlobalUnlock(hGlobal);
            if (SetClipboardData(CF_UNICODETEXT, hGlobal) == 0)
            {
                GlobalFree(hGlobal); // ownership only transfers on success
                return false;
            }
            return true;
        });
        return ok;
    }

    public bool SendPaste()
    {
        var inputs = new INPUT[4];
        inputs[0].ki.wVk = VK_CONTROL;
        inputs[1].ki.wVk = VK_V;
        inputs[2].ki.wVk = VK_V;
        inputs[2].ki.dwFlags = KEYEVENTF_KEYUP;
        inputs[3].ki.wVk = VK_CONTROL;
        inputs[3].ki.dwFlags = KEYEVENTF_KEYUP;
        for (var i = 0; i < inputs.Length; i++)
            inputs[i].type = INPUT_KEYBOARD;

        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) == inputs.Length;
    }

    public Task SettleAsync(CancellationToken cancellationToken = default) =>
        Task.Delay(SettleDelay, cancellationToken);

    private static T WithClipboard<T>(Func<T> action)
    {
        for (var attempt = 0; ; attempt++)
        {
            if (OpenClipboard(0))
            {
                try
                {
                    return action();
                }
                finally
                {
                    CloseClipboard();
                }
            }

            if (attempt >= ClipboardRetries)
                return default!;
            Thread.Sleep(ClipboardRetryDelayMs);
        }
    }

    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 2;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_V = 0x56;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public KEYBDINPUT ki;
        private readonly ulong _padding; // MOUSEINPUT is larger; keep union size
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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(nint hWndNewOwner);

    [DllImport("user32.dll")]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll")]
    private static extern nint GetClipboardData(uint uFormat);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetClipboardData(uint uFormat, nint hMem);

    [DllImport("kernel32.dll")]
    private static extern nint GlobalAlloc(uint uFlags, nuint dwBytes);

    [DllImport("kernel32.dll")]
    private static extern nint GlobalLock(nint hMem);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(nint hMem);

    [DllImport("kernel32.dll")]
    private static extern nint GlobalFree(nint hMem);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
}
