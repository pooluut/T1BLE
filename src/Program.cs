namespace T1BLE;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        using var instance = new Mutex(true, @"Local\T1BLE.BackgroundVoice", out bool created);
        if (!created) return;
        try
        {
            int index = Array.IndexOf(args, "--address");
            Application.Run(new BackgroundVoiceContext(index >= 0 && index + 1 < args.Length
                ? args[index + 1] : "88:BF:7A:4F:E5:42"));
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "后台语音启动失败"); }
    }
}
