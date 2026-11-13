using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.Text.Json;

namespace T1BLE;

internal static class AudioBridgeTest
{
    public static async Task RunAsync()
    {
        var resultPath = Path.Combine(Environment.CurrentDirectory, "logs", "audio-bridge-test.json");
        Directory.CreateDirectory(Path.GetDirectoryName(resultPath)!);
        try
        {
            using var devices = new MMDeviceEnumerator();
            using var input = devices.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                .First(d => d.FriendlyName == "CABLE Output (VB-Audio Virtual Cable)");
            using var defaultInput = devices.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
            using var capture = new WasapiCapture(input);
            using var bridge = new DictationBridge();
            var gate = new object();
            double energy = 0;
            long samples = 0;
            bool floatSamples = capture.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat ||
                capture.WaveFormat is WaveFormatExtensible f && f.SubFormat == new Guid("00000003-0000-0010-8000-00aa00389b71");
            capture.DataAvailable += (_, e) =>
            {
                lock (gate)
                {
                    for (int i = 0; i + capture.WaveFormat.BitsPerSample / 8 <= e.BytesRecorded; i += capture.WaveFormat.BitsPerSample / 8)
                    {
                        double value = floatSamples ? BitConverter.ToSingle(e.Buffer, i)
                            : capture.WaveFormat.BitsPerSample == 16 ? BitConverter.ToInt16(e.Buffer, i) / 32768.0
                            : BitConverter.ToInt32(e.Buffer, i) / 2147483648.0;
                        energy += value * value; samples++;
                    }
                }
            };
            capture.StartRecording();
            bridge.Start(2);
            // A known signal validates routing without sending Win+H or changing defaults.
            for (int block = 0; block < 100; block++)
            {
                var pcm = new byte[640];
                for (int sample = 0; sample < 320; sample++)
                {
                    short value = (short)(Math.Sin(2 * Math.PI * 440 * (block * 320 + sample) / 16000) * 3000);
                    pcm[2 * sample] = (byte)value; pcm[2 * sample + 1] = (byte)(value >> 8);
                }
                bridge.Write(pcm);
                await Task.Delay(20);
            }
            await Task.Delay(300);
            bridge.Stop();
            var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            capture.RecordingStopped += (_, _) => stopped.TrySetResult();
            capture.StopRecording();
            await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
            double rms;
            lock (gate) rms = samples == 0 ? 0 : Math.Sqrt(energy / samples);
            File.WriteAllText(resultPath, JsonSerializer.Serialize(new
            {
                passed = samples > 0 && rms > 0.001, samples, rms,
                render = bridge.DeviceName, capture = input.FriendlyName,
                defaultMicrophone = defaultInput.FriendlyName,
                capture.WaveFormat.SampleRate, capture.WaveFormat.BitsPerSample
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            File.WriteAllText(resultPath, JsonSerializer.Serialize(new { passed = false, error = ex.ToString() }));
            Environment.ExitCode = 1;
        }
    }
}
