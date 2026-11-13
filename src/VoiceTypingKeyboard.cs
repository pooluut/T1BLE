using System.ComponentModel;
using System.Runtime.InteropServices;

namespace T1BLE;

internal sealed partial class VoiceTypingKeyboard : IDisposable
{
    private readonly HookProc callback;
    private readonly Action selectComputer;
    private readonly SynchronizationContext context;
    private IntPtr hook;
    private bool leftWindows, rightWindows;

    public VoiceTypingKeyboard(Action selectComputer)
    {
        this.selectComputer = selectComputer;
        context = SynchronizationContext.Current ?? throw new InvalidOperationException("键盘监听需要 UI 消息线程。");
        callback = OnKey;
        hook = SetWindowsHookEx(13, callback, GetModuleHandle(null), 0);
        if (hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private IntPtr OnKey(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0)
        {
            int key = Marshal.ReadInt32(data), flags = Marshal.ReadInt32(data, 8);
            // SendInput from the remote must never change the selected source.
            if ((flags & 0x10) == 0)
            {
                bool down = message == (IntPtr)0x100 || message == (IntPtr)0x104;
                if (key == 0x5B) leftWindows = down;
                if (key == 0x5C) rightWindows = down;
                if (down && key == 0x48 && (leftWindows || rightWindows))
                {
                    // Defer all audio work beyond the low-level hook callback.
                    context.Post(_ => selectComputer(), null);
                }
            }
        }
        return CallNextHookEx(hook, code, message, data);
    }

    public void Dispose() { if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; } }

    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
}
