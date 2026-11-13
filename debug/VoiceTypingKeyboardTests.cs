using System.Runtime.InteropServices;
namespace T1BLE;
internal sealed partial class VoiceTypingKeyboard
{
    internal static void SelfTest()
    {
        using var form = new Form { ShowInTaskbar = false, Opacity = 0 };
        form.Load += (_, _) =>
        {
            int selected = 0;
            var checks = new List<object>();
            using var keyboard = new VoiceTypingKeyboard(() => selected++);
            var packet = Marshal.AllocHGlobal(24);
            try
            {
                void Send(int key, bool down, bool injected = false)
                {
                    for (int i = 0; i < 24; i += 4) Marshal.WriteInt32(packet, i, 0);
                    Marshal.WriteInt32(packet, key);
                    Marshal.WriteInt32(packet, 8, injected ? 0x10 : 0);
                    keyboard.OnKey(0, (IntPtr)(down ? 0x100 : 0x101), packet);
                }
                void Check(string name, int expected)
                {
                    Application.DoEvents();
                    checks.Add(new { name, passed = selected == expected, selected, expected });
                    if (selected != expected) throw new InvalidOperationException(name);
                }
                Send(0x48, true); Send(0x48, false);
                Check("H alone does not select computer", 0);
                Send(0x5B, true, true); Send(0x48, true, true);
                Send(0x48, false, true); Send(0x5B, false, true);
                Check("Remote SendInput Win+H does not select computer", 0);
                Send(0x5B, true); Send(0x48, true); Send(0x48, false); Send(0x5B, false);
                Check("Physical left Win+H selects computer once", 1);
                Send(0x5C, true); Send(0x48, true); Send(0x48, false); Send(0x5C, false);
                Check("Physical right Win+H selects computer once", 2);
                Send(0x48, true); Send(0x48, false);
                Check("Released Windows keys do not remain active", 2);
                Send(0x5B, true); Send(0x48, true, true); Send(0x48, false, true); Send(0x5B, false);
                Check("Injected H with physical Win held is ignored", 2);
                Directory.CreateDirectory("logs");
                File.WriteAllText("logs/source-selection-test.json", System.Text.Json.JsonSerializer.Serialize(
                    new { passed = true, checks }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                Environment.ExitCode = 1;
                Directory.CreateDirectory("logs");
                File.WriteAllText("logs/source-selection-test.json", System.Text.Json.JsonSerializer.Serialize(
                    new { passed = false, error = ex.Message, checks }));
            }
            finally { Marshal.FreeHGlobal(packet); form.Close(); }
        };
        Application.Run(form);
    }
}
