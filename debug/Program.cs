namespace T1BLE;

internal static class TestProgram
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--tray-window-test"))
        {
            ApplicationConfiguration.Initialize();
            using var context = new BackgroundVoiceContext("88:BF:7A:4F:E5:42");
            using var timer = new System.Windows.Forms.Timer { Interval = 200 };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (context.MainForm is not null || Application.OpenForms.Count != 0)
                    throw new Exception("托盘程序不应创建任何 Form。");
                bool posted = false;
                SynchronizationContext.Current!.Post(_ => posted = true, null);
                Application.DoEvents();
                if (!posted) throw new Exception("无窗体消息分发失败。");
                Console.WriteLine("Tray window test passed.");
                context.ExitThread();
            };
            timer.Start();
            Application.Run(context);
            return;
        }
        if (args.Contains("--logging-mode-test"))
        {
            using var router = new BackgroundAudioRouter(_ => { });
            using var capture = new BleCapture(_ => { }, router);
            capture.RunAsync("invalid-address", CancellationToken.None).GetAwaiter().GetResult();
#if DEBUG
            if (capture.LogPath is null || !File.Exists(capture.LogPath))
                throw new Exception("调试版没有生成日志。");
#else
            if (capture.LogPath is not null) throw new Exception("正式版启用了日志。");
#endif
            Console.WriteLine("Logging mode test passed.");
            return;
        }
        if (args.Contains("--performance-probe"))
        {
            PerformanceProbe.Run();
            return;
        }
        if (args.Contains("--decoder-test"))
        {
            byte[] actual = [];
            var decoder = new AdpcmDecoder { PcmReady = pcm => actual = pcm };
            void Check(byte packet, params short[] expected)
            {
                decoder.Write([packet]);
                var samples = Enumerable.Range(0, actual.Length / 2)
                    .Select(i => BitConverter.ToInt16(actual, i * 2));
                if (!samples.SequenceEqual(expected)) throw new InvalidOperationException("ADPCM 解码结果不匹配。");
            }
            Check(0x17, 1, 12);
            decoder.Sync(-100, 0);
            Check(0x80, -100, -100);
            decoder.Sync(32760, 88);
            Check(0x77, 32767, 32767);
            decoder.Sync(-32760, 88);
            Check(0xFF, -32768, -32768);
            byte[] compressed = [0x17, 0x80, 0xFF, 0x43, 0x28, 0x77, 0x00];
            byte[] whole = [];
            var contiguous = new AdpcmDecoder { PcmReady = pcm => whole = pcm };
            contiguous.Write(compressed);
            var split = new List<byte>();
            var fragmented = new AdpcmDecoder { PcmReady = split.AddRange };
            fragmented.Write(compressed[..2]);
            fragmented.Write(compressed[2..5]);
            fragmented.Write(compressed[5..]);
            if (!whole.SequenceEqual(split)) throw new Exception("分包改变了连续解码状态。");
            try { decoder.Sync(0, 89); throw new Exception("无效同步索引未拒绝。"); }
            catch (InvalidDataException) { }
            Directory.CreateDirectory("logs");
            File.WriteAllText("logs/decoder-test.json", "{\"passed\":true}");
            return;
        }
        if (args.Contains("--ble-filter-test"))
        {
            if (!BleCapture.IsVoiceNotification(new Guid("ab5e0003-5a21-4f05-bc7d-af01f617b664")) ||
                !BleCapture.IsVoiceNotification(new Guid("ab5e0004-5a21-4f05-bc7d-af01f617b664")) ||
                BleCapture.IsVoiceNotification(new Guid("ab5e0002-5a21-4f05-bc7d-af01f617b664")) ||
                BleCapture.IsVoiceNotification(new Guid("00002a19-0000-1000-8000-00805f9b34fb")))
                throw new Exception("BLE 通知范围不正确。");
            Console.WriteLine("BLE notification filter test passed.");
            return;
        }
        if (args.Contains("--microphone-idle-test"))
        {
            Directory.CreateDirectory("logs");
            try
            {
                using var router = new BackgroundAudioRouter(_ => { });
                Thread.Sleep(300);
                AssertNoCapture();
                router.SelectComputer();
                Thread.Sleep(300);
                router.ReleaseComputer();
                Thread.Sleep(300);
                AssertNoCapture();
                router.RequestRemote();
                router.Start(2);
                AssertNoCapture();
                router.Stop();
                void AssertNoCapture()
                {
                using var devices = new NAudio.CoreAudioApi.MMDeviceEnumerator();
                foreach (var device in devices.EnumerateAudioEndPoints(NAudio.CoreAudioApi.DataFlow.Capture, NAudio.CoreAudioApi.DeviceState.Active))
                {
                    using (device)
                    {
                        var sessions = device.AudioSessionManager.Sessions;
                        for (int i = 0; i < sessions.Count; i++)
                        {
                            using var session = sessions[i];
                            if (session.GetProcessID == Environment.ProcessId && session.State == NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateActive)
                                throw new InvalidOperationException("后台闲置时仍存在本程序的活动录音会话：" + device.FriendlyName);
                        }
                    }
                }
                }
                File.WriteAllText("logs/microphone-idle-test.json", "{\"passed\":true}");
            }
            catch (Exception ex)
            {
                Environment.ExitCode = 1;
                File.WriteAllText("logs/microphone-idle-test.json", System.Text.Json.JsonSerializer.Serialize(new { passed = false, error = ex.Message }));
            }
            return;
        }
        if (args.Contains("--background-audio-test"))
        {
            var failures = new System.Collections.Concurrent.ConcurrentQueue<string>();
            Directory.CreateDirectory("logs");
            try
            {
                using (var router = new BackgroundAudioRouter(failures.Enqueue))
                {
                    Thread.Sleep(300);
                    router.RequestRemote();
                    router.Start(2);
                    router.Write(new byte[3200]);
                    Thread.Sleep(150);
                    router.Stop();
                    router.SelectComputer();
                    Thread.Sleep(300);
                    router.RequestRemote();
                    router.Start(1);
                    router.Write(new byte[1600]);
                    Thread.Sleep(150);
                    router.Stop();
                    router.SelectComputer();
                    Thread.Sleep(300);
                }
                if (!failures.IsEmpty) throw new InvalidOperationException(string.Join("; ", failures));
                File.WriteAllText("logs/background-audio-test.json", "{\"passed\":true,\"remoteSampleRates\":[8000,16000],\"computerRestored\":true}");
            }
            catch (Exception ex)
            {
                Environment.ExitCode = 1;
                File.WriteAllText("logs/background-audio-test.json", System.Text.Json.JsonSerializer.Serialize(new { passed = false, error = ex.Message }));
            }
            return;
        }
        if (args.Contains("--source-selection-test"))
        {
            ApplicationConfiguration.Initialize();
            VoiceTypingKeyboard.SelfTest();
            return;
        }
        if (args.Contains("--search-guard-test"))
        {
            ApplicationConfiguration.Initialize();
            SearchKeyGuard.SelfTest();
            return;
        }
        if (args.Contains("--audio-test"))
        {
            Task.Run(() => AudioBridgeTest.RunAsync()).GetAwaiter().GetResult();
            return;
        }
        throw new ArgumentException("请指定测试参数，参见 README.md。");
    }
}
