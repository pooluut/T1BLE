using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace T1BLE;

internal sealed class BleCapture(Action<string> emit, IRemoteAudioSink bridge) : IDisposable
{
    private readonly object gate = new();
    private StreamWriter? writer;
    private readonly SemaphoreSlim notificationReady = new(0, 1);
    public bool CapabilitiesConfirmed { get; private set; }
    public string? LogPath { get; private set; }
    private readonly ConcurrentQueue<(Guid Uuid, byte[] Data)> incoming = new();
    private static readonly Guid VoiceService = new("ab5e0001-5a21-4f05-bc7d-af01f617b664");
    private static readonly Guid Tx = new("ab5e0002-5a21-4f05-bc7d-af01f617b664");
    private static readonly Guid Audio = new("ab5e0003-5a21-4f05-bc7d-af01f617b664");
    private static readonly Guid Control = new("ab5e0004-5a21-4f05-bc7d-af01f617b664");

    [Conditional("DEBUG")]
    private void Record(string kind, object? data = null)
    {
        lock (gate)
        {
            if (writer is null) return;
            var line = JsonSerializer.Serialize(new { time = DateTimeOffset.Now, kind, data });
            writer.WriteLine(line);
            writer.Flush();
        }
    }

    public async Task RunAsync(string address, CancellationToken token)
    {
#if DEBUG
        var directory = Path.Combine(AppContext.BaseDirectory, "logs");
        if (File.Exists(Path.Combine(Environment.CurrentDirectory, "T1BLE.csproj")))
            directory = Path.Combine(Environment.CurrentDirectory, "logs");
        Directory.CreateDirectory(directory);
        LogPath = Path.Combine(directory, $"{DateTime.Now:yyyyMMdd-HHmmss-fffffff}.jsonl");
        writer = new StreamWriter(LogPath, false, new UTF8Encoding(false));
#endif
        var services = new List<GattDeviceService>();
        var subscribed = new List<GattCharacteristic>();
        GattCharacteristic? tx = null;
        AdpcmDecoder? recording = null;
        byte codec = 0, streamId = 0;
        bool versionSupported = false, micRequested = false;
        long openedAt = 0;
        long extendedAt = 0;
        bool typingRequested = false;
        void StopTyping()
        {
            bridge.Stop();
            if (typingRequested)
            {
                typingRequested = false;
                // Win+H is a toggle; Windows may already have stopped on silence.
                emit("typing_stop_required");
                Record("typing_stop_required", new { message = "遥控器已停止。请在 Windows 语音输入面板确认停止状态。" });
            }
        }
        async Task Send(byte[] command)
        {
            if (tx is null) throw new InvalidOperationException("缺少 ATVV 命令接口");
            var status = await tx.WriteValueAsync(command.AsBuffer(), GattWriteOption.WriteWithoutResponse)
                .AsTask().WaitAsync(TimeSpan.FromSeconds(3));
            Record("command", new { hex = Convert.ToHexString(command), status = status.ToString() });
            if (status != GattCommunicationStatus.Success) throw new IOException($"ATVV 写入失败：{status}");
        }
        using var disconnected = CancellationTokenSource.CreateLinkedTokenSource(token);
        try
        {
            Record("session", new { address, log = LogPath });
            var number = ulong.Parse(address.Replace(":", "").Replace("-", ""), NumberStyles.HexNumber);
            using var device = await BluetoothLEDevice.FromBluetoothAddressAsync(number).AsTask(token);
            if (device is null) throw new InvalidOperationException("找不到已配对设备，请按普通按钮唤醒后重试。");
            using var session = await GattSession.FromDeviceIdAsync(device.BluetoothDeviceId).AsTask(token);
            if (session is not null) session.MaintainConnection = true;
            void ConnectionChanged(BluetoothLEDevice sender, object args)
            {
                Record("connection", new { status = sender.ConnectionStatus.ToString() });
                if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected) disconnected.Cancel();
            }
            device.ConnectionStatusChanged += ConnectionChanged;
            try
            {
                Record("connected_device", new { device.Name, status = device.ConnectionStatus.ToString() });
                var result = await device.GetGattServicesForUuidAsync(VoiceService, BluetoothCacheMode.Uncached).AsTask(token);
                if (result.Status != GattCommunicationStatus.Success)
                    throw new InvalidOperationException($"服务枚举失败：{result.Status}");
                services.AddRange(result.Services);
                foreach (var service in services)
                {
                    token.ThrowIfCancellationRequested();
                    Record("service", new { uuid = service.Uuid });
                    var chars = await service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached).AsTask(token);
                    if (chars.Status != GattCommunicationStatus.Success)
                    {
                        Record("service_error", new { uuid = service.Uuid, status = chars.Status.ToString() });
                        continue;
                    }
                    foreach (var characteristic in chars.Characteristics)
                    {
                        var properties = characteristic.CharacteristicProperties;
                        if (characteristic.Uuid == Tx) tx = characteristic;
                        Record("characteristic", new { service = service.Uuid, uuid = characteristic.Uuid,
                            handle = characteristic.AttributeHandle, properties = properties.ToString() });
                        if (!IsVoiceNotification(characteristic.Uuid)) continue;
                        var mode = properties.HasFlag(GattCharacteristicProperties.Notify)
                            ? GattClientCharacteristicConfigurationDescriptorValue.Notify
                            : properties.HasFlag(GattCharacteristicProperties.Indicate)
                                ? GattClientCharacteristicConfigurationDescriptorValue.Indicate
                                : GattClientCharacteristicConfigurationDescriptorValue.None;
                        if (mode == GattClientCharacteristicConfigurationDescriptorValue.None) continue;
                        characteristic.ValueChanged += Notification;
                        try
                        {
                            var status = await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(mode).AsTask(token);
                            if (status != GattCommunicationStatus.Success) throw new InvalidOperationException(status.ToString());
                            subscribed.Add(characteristic);
                            Record("subscribed", new { uuid = characteristic.Uuid, handle = characteristic.AttributeHandle });
                        }
                        catch (Exception ex)
                        {
                            characteristic.ValueChanged -= Notification;
                            if (token.IsCancellationRequested) throw;
                            Record("subscribe_error", new { uuid = characteristic.Uuid, error = ex.Message });
                        }
                    }
                }
                // §3: version 1.0, legacy constant 0x0003, on-request interaction.
                if (tx is not null) await Send([0x0A, 0x01, 0x00, 0x00, 0x03, 0x00]);
                Record("ready", new { subscriptions = subscribed.Count,
                    message = "能力确认后，按一次语音键并讲话；再按一次停止收音（最多60秒）。" });
                while (!disconnected.IsCancellationRequested)
                {
                    while (incoming.TryDequeue(out var packet))
                    {
                        var data = packet.Data;
                        if (packet.Uuid == Audio)
                        {
                            recording?.Write(data);
                            continue;
                        }
                        if (packet.Uuid != Control || data.Length == 0) continue;
                        if (data[0] == 0x0B && data.Length >= 9)
                        {
                            versionSupported = data[1] == 1 && data[2] == 0;
                            CapabilitiesConfirmed = versionSupported;
                            if (versionSupported) emit("capabilities");
                            Record("capabilities", new { version = $"{data[1]}.{data[2]}", codecs = data[3],
                                interaction = data[4], frameSize = (data[5] << 8) | data[6], versionSupported });
                        }
                        else if (data[0] == 0x08)
                        {
                            Record("start_search");
                            if (!versionSupported) { Record("voice_blocked", new { reason = "尚未确认协议 1.0 能力" }); continue; }
                            if (micRequested)
                            {
                                await Send([0x0D, streamId]);
                                micRequested = false;
                                recording = null;
                                StopTyping();
                                continue;
                            }
                            bridge.RequestRemote();
                            micRequested = true;
                            streamId = 0;
                            openedAt = Stopwatch.GetTimestamp();
                            extendedAt = openedAt;
                            await Send([0x0C, 0x00]);
                        }
                        else if (data[0] == 0x04 && data.Length >= 4)
                        {
                            recording = null;
                            codec = data[2]; streamId = data[3];
                            micRequested = true;
                            openedAt = Stopwatch.GetTimestamp();
                            if (codec is 1 or 2)
                            {
                                bridge.Start(codec);
                                recording = new AdpcmDecoder { PcmReady = bridge.Write };
                                if (!typingRequested && bridge.ShouldActivate)
                                {
                                    DictationBridge.ToggleVoiceTyping();
                                    typingRequested = true;
                                    emit("typing_requested");
                                    Record("typing_requested", new { device = bridge.DeviceName });
                                }
                                Record("audio_started", new { codec, streamId });
                            }
                            else
                            {
                                Record("unsupported_codec", new { codec });
                                await Send([0x0D, streamId]); micRequested = false; StopTyping();
                            }
                        }
                        else if (data[0] == 0x0A && data.Length >= 7 && recording is not null)
                        {
                            if (data[1] != codec)
                            {
                                recording = null; codec = data[1];
                                if (codec is 1 or 2)
                                {
                                    bridge.Start(codec);
                                    recording = new AdpcmDecoder { PcmReady = bridge.Write };
                                }
                                else { await Send([0x0D, streamId]); micRequested = false; StopTyping(); }
                            }
                            recording?.Sync(unchecked((short)((data[4] << 8) | data[5])), data[6]);
                            Record("audio_sync", new { codec, frame = (data[2] << 8) | data[3] });
                        }
                        else if (data[0] == 0x00)
                        {
                            recording = null; micRequested = false;
                            StopTyping();
                            Record("audio_stopped", new { reason = data.Length > 1 ? data[1] : -1 });
                        }
                        else if (data[0] == 0x0C)
                        {
                            micRequested = false;
                            recording = null;
                            StopTyping();
                            Record("mic_open_error", new { hex = Convert.ToHexString(data) });
                        }
                    }
                    if (micRequested && Stopwatch.GetElapsedTime(extendedAt) >= TimeSpan.FromSeconds(5))
                    {
                        await Send([0x0E, streamId]);
                        extendedAt = Stopwatch.GetTimestamp();
                    }
                    const int limit = 60;
                    if (micRequested && Stopwatch.GetElapsedTime(openedAt) >= TimeSpan.FromSeconds(limit))
                    {
                        await Send([0x0D, streamId]);
                        micRequested = false;
                        recording = null;
                        StopTyping();
                        Record("capture_limit", new { seconds = limit });
                    }
                    int waitMilliseconds = Timeout.Infinite;
                    if (micRequested)
                    {
                        double untilExtend = 5000 - Stopwatch.GetElapsedTime(extendedAt).TotalMilliseconds;
                        double untilLimit = 60000 - Stopwatch.GetElapsedTime(openedAt).TotalMilliseconds;
                        waitMilliseconds = (int)Math.Clamp(Math.Ceiling(Math.Min(untilExtend, untilLimit)), 1, 5000);
                    }
                    await notificationReady.WaitAsync(waitMilliseconds, disconnected.Token);
                }
            }
            finally
            {
                if (micRequested && tx is not null)
                {
                    try { await Send([0x0D, streamId]); }
                    catch (Exception ex) { Record("mic_close_error", new { error = ex.Message }); }
                }
                StopTyping();
                device.ConnectionStatusChanged -= ConnectionChanged;
                foreach (var characteristic in subscribed)
                {
                    characteristic.ValueChanged -= Notification;
                    try
                    {
                        await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                            GattClientCharacteristicConfigurationDescriptorValue.None).AsTask().WaitAsync(TimeSpan.FromSeconds(2));
                    }
                    catch (Exception ex) { Record("cleanup_error", new { error = ex.Message }); }
                }
            }
        }
        catch (OperationCanceledException) { Record("stopped"); }
        catch (Exception ex) { Record("error", new { error = ex.Message }); }
        finally
        {
            foreach (var service in services) service.Dispose();
            Record("session_closed", new { log = LogPath });
            Dispose();
        }
    }

    private void Notification(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        if (!IsVoiceNotification(sender.Uuid)) return;
        using var reader = DataReader.FromBuffer(args.CharacteristicValue);
        var bytes = new byte[reader.UnconsumedBufferLength];
        reader.ReadBytes(bytes);
        incoming.Enqueue((sender.Uuid, bytes));
        try { notificationReady.Release(); }
        catch (SemaphoreFullException) { } // Multiple packets share one pending wakeup.
        if (sender.Uuid == Audio) return;
        Record("notification", new { uuid = sender.Uuid, handle = sender.AttributeHandle,
            length = bytes.Length, hex = Convert.ToHexString(bytes) });
    }

    internal static bool IsVoiceNotification(Guid uuid) => uuid == Control || uuid == Audio;

    public void Dispose() { lock (gate) { writer?.Dispose(); writer = null; } }
}
