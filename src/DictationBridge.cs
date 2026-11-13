using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.Runtime.InteropServices;

namespace T1BLE;

internal interface IRemoteAudioSink
{
    string DeviceName { get; }
    bool ShouldActivate => true;
    void RequestRemote();
    void Start(byte codec);
    void Write(byte[] pcm);
    void Stop();
}

internal sealed class DictationBridge : IDisposable, IRemoteAudioSink
{
    private readonly MMDeviceEnumerator devices = new();
    private readonly MMDevice cable;
    private WasapiOut? player;
    private BufferedWaveProvider? buffer;
    public string DeviceName => cable.FriendlyName;
    public void RequestRemote() { }

    public DictationBridge()
    {
        cable = devices.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
            .FirstOrDefault(d => d.FriendlyName.Equals("CABLE Input (VB-Audio Virtual Cable)", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("未找到启用的 CABLE Input (VB-Audio Virtual Cable)。");
    }

    public void Start(byte codec)
    {
        Stop();
        buffer = new BufferedWaveProvider(new WaveFormat(codec == 1 ? 8000 : 16000, 16, 1))
        { BufferDuration = TimeSpan.FromSeconds(3), DiscardOnBufferOverflow = false, ReadFully = true };
        player = new WasapiOut(cable, AudioClientShareMode.Shared, true, 80);
        player.Init(buffer);
        player.Play();
    }

    public void Write(byte[] pcm) => buffer?.AddSamples(pcm, 0, pcm.Length);
    public void Stop()
    {
        player?.Stop(); player?.Dispose(); player = null; buffer = null;
    }
    public void Dispose()
    {
        try { Stop(); }
        finally { cable.Dispose(); devices.Dispose(); }
    }

    public static void ToggleVoiceTyping()
    {
        var keys = new[] { Key(0x5B, 0), Key(0x48, 0), Key(0x48, 2), Key(0x5B, 2) };
        if (SendInput((uint)keys.Length, keys, Marshal.SizeOf<Input>()) != keys.Length)
            throw new InvalidOperationException("Win+H 发送失败。请在普通权限的文本窗口中测试。");
    }

    private static Input Key(ushort code, uint flags) => new()
    { Type = 1, Data = new InputData { Keyboard = new Keyboard { Code = code, Flags = flags } } };
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputData Data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputData
    {
        [FieldOffset(0)] public Keyboard Keyboard;
        [FieldOffset(0)] public Mouse Mouse;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Keyboard
    { public ushort Code, Scan; public uint Flags, Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct Mouse
    { public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra; }
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);
}
