using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace T1BLE;

// All selection operations run on the application message-loop thread.
// Capture callbacks only touch the buffer under the gate.
internal sealed class BackgroundAudioRouter : IRemoteAudioSink, IDisposable
{
    private readonly object gate = new();
    private readonly MMDeviceEnumerator devices = new();
    private readonly DictationBridge remote;
    private readonly MMDevice microphone;
    private readonly MMDevice cable;
    private WasapiCapture? capture;
    private WasapiOut? player;
    private BufferedWaveProvider? buffer;
    private bool computer;
    private bool remoteRequested;
    private bool disposed;
    private readonly Action<string> fault;
    private DateTime computerRequestedAt;
    private bool observedListening;
    public event Action? ComputerReleased;
    public bool IsComputerActive => capture is not null;
    public string DeviceName => remote.DeviceName;
    public bool ShouldActivate => remoteRequested;
    public string MicrophoneName => microphone.FriendlyName;

    public BackgroundAudioRouter(Action<string> fault)
    {
        this.fault = fault;
        // Validate before opening any stream; never allow a cable feedback loop.
        microphone = devices.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
        if (microphone.FriendlyName.Contains("CABLE", StringComparison.OrdinalIgnoreCase) ||
            microphone.FriendlyName.Contains("Voicemeeter", StringComparison.OrdinalIgnoreCase))
        {
            microphone.Dispose(); devices.Dispose();
            throw new InvalidOperationException("请先将系统默认输入设为电脑的实体麦克风，再启动后台模式。");
        }
        try
        {
            remote = new DictationBridge();
            cable = devices.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                .First(d => d.FriendlyName.Equals(DeviceName, StringComparison.OrdinalIgnoreCase));

        }
        catch { Dispose(); throw; }
    }

    public void SelectComputer()
    {
        remoteRequested = false;
        remote.Stop();
        ReleaseComputer();
        computerRequestedAt = DateTime.UtcNow;
        observedListening = false;
        computer = true;
        try
        {
            capture = new WasapiCapture(microphone);
            buffer = new BufferedWaveProvider(capture.WaveFormat)
            { BufferDuration = TimeSpan.FromMilliseconds(500), DiscardOnBufferOverflow = false };
            player = new WasapiOut(cable, AudioClientShareMode.Shared, true, 60);
            player.Init(buffer);
            var currentBuffer = buffer;
            capture.DataAvailable += (_, e) =>
            {
                lock (gate)
                {
                    if (disposed || !computer) return;
                    try { currentBuffer.AddSamples(e.Buffer, 0, e.BytesRecorded); }
                    catch (Exception ex) { computer = false; fault(ex.Message); }
                }
            };
            capture.RecordingStopped += (_, e) => { if (e.Exception is not null) fault(e.Exception.Message); };
            player.Play();
            capture.StartRecording();
        }
        catch { ReleaseComputer(); throw; }
    }

    public void RequestRemote()
    {
        ReleaseComputer();
        remoteRequested = true;
    }

    public void Start(byte codec) { if (remoteRequested) remote.Start(codec); }
    public void Write(byte[] pcm) { if (remoteRequested) remote.Write(pcm); }
    // Keep the cable silent on remote stop/disconnect. Windows may still be listening.
    public void Stop() { remoteRequested = false; remote.Stop(); }

    public void ReleaseComputer()
    {
        lock (gate) { computer = false; }
        capture?.StopRecording(); capture?.Dispose(); capture = null;
        player?.Dispose(); player = null; buffer = null;
        ComputerReleased?.Invoke();
    }

    public void PollVoiceTyping()
    {
        if (capture is null) return;
        try
        {
            bool listening = false;
            // WASAPI session enumeration observes capture activity without opening a microphone.
            using var endpoint = devices.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                .FirstOrDefault(d => d.FriendlyName.Equals("CABLE Output (VB-Audio Virtual Cable)", StringComparison.OrdinalIgnoreCase));
            if (endpoint is not null)
            {
                var sessions = endpoint.AudioSessionManager.Sessions;
                for (int i = 0; i < sessions.Count; i++)
                {
                    using var session = sessions[i];
                    if (session.State != NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateActive) continue;
                    try
                    {
                        using var process = System.Diagnostics.Process.GetProcessById((int)session.GetProcessID);
                        if (process.ProcessName.Equals("TextInputHost", StringComparison.OrdinalIgnoreCase)) listening = true;
                    }
                    catch (ArgumentException) { }
                }
            }
            if (listening) observedListening = true;
            else if (observedListening || DateTime.UtcNow - computerRequestedAt > TimeSpan.FromSeconds(5))
                ReleaseComputer();
        }
        catch { ReleaseComputer(); }
    }

    public void Dispose()
    {
        lock (gate) { disposed = true; computer = false; }
        ReleaseComputer();
        remote?.Dispose(); cable?.Dispose(); microphone.Dispose(); devices.Dispose();
    }
}
