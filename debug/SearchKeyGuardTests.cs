using System.Runtime.InteropServices;
namespace T1BLE;
internal sealed partial class SearchKeyGuard
{
    private int suppressed;
    partial void OnSuppressed() => suppressed++;

    internal static void SelfTest()
    {
        using var form = new Form { ShowInTaskbar = false, Opacity = 0 };
        using var timer = new System.Windows.Forms.Timer { Interval = 200 };
        SearchKeyGuard? guard = null;
        int phase = 0;
        form.Load += (_, _) => { guard = new SearchKeyGuard(); timer.Start(); };
        timer.Tick += (_, _) =>
        {
            if (phase++ == 0)
            {
                keybd_event(0xAA, 0, 0, UIntPtr.Zero);
                keybd_event(0xAA, 0, 2, UIntPtr.Zero);
                return;
            }
            timer.Stop();
            int count = guard!.suppressed;
            guard.Dispose();
            Directory.CreateDirectory("logs");
            File.WriteAllText("logs/search-guard-test.json",
                System.Text.Json.JsonSerializer.Serialize(new { passed = count == 2, suppressed = count }));
            Environment.ExitCode = count == 2 ? 0 : 1;
            form.Close();
        };
        Application.Run(form);
    }

    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
}
