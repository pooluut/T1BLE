using System.ComponentModel;
using System.Runtime.InteropServices;

namespace T1BLE;

// Install on the WinForms message-loop thread before BLE notifications start.
// WH_KEYBOARD_LL cannot identify the source device. Only VK_BROWSER_SEARCH
// is suppressed, and only while a dictation session owns this guard.
internal sealed partial class SearchKeyGuard : IDisposable
{
    private readonly HookProc callback;
    private readonly Action<string> report;
    private IntPtr hook;
    public int Suppressed { get; private set; }

    public SearchKeyGuard(Action<string> report)
    {
        this.report = report;
        callback = OnKey;
        hook = SetWindowsHookEx(13, callback, GetModuleHandle(null), 0);
        if (hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private IntPtr OnKey(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && Marshal.ReadInt32(data) == 0xAA)
        {
            Suppressed++;
            // Avoid logging or I/O within the low-level hook timeout.
            var context = SynchronizationContext.Current;
            context?.Post(_ => report("已拦截原生搜索键，保留当前输入焦点。"), null);
            return new IntPtr(1);
        }
        return CallNextHookEx(hook, code, message, data);
    }

    public void Dispose()
    {
        if (hook == IntPtr.Zero) return;
        UnhookWindowsHookEx(hook);
        hook = IntPtr.Zero;
    }

    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
}
