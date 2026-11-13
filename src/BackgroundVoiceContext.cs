
namespace T1BLE;

internal sealed class BackgroundVoiceContext : ApplicationContext
{
    private readonly NotifyIcon tray;
    private readonly System.Drawing.Icon applicationIcon;
    private readonly CancellationTokenSource stop = new();
    private readonly BackgroundAudioRouter router;
    private readonly VoiceTypingKeyboard keyboard;
    private readonly SearchKeyGuard search;
    private readonly string address;
    private readonly WindowsFormsSynchronizationContext uiContext;
    private bool exiting;
    private readonly System.Windows.Forms.Timer microphoneTimer = new() { Interval = 250 };

    public BackgroundVoiceContext(string address)
    {
        this.address = address;
        // WinForms provides its own message dispatch infrastructure; no application Form is needed.
        uiContext = new WindowsFormsSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(uiContext);
        using var iconStream = typeof(BackgroundVoiceContext).Assembly.GetManifestResourceStream("T1BLE.AppIcon")!;
        applicationIcon = new System.Drawing.Icon(iconStream);
        tray = new NotifyIcon { Icon = applicationIcon, Text = "T1 语音 · 正在启动", Visible = true };
        try
        {
            router = new BackgroundAudioRouter(error => uiContext.Post(_ => Fail(error), null));
            microphoneTimer.Tick += (_, _) =>
            {
                router.PollVoiceTyping();
                if (!router.IsComputerActive) microphoneTimer.Stop();
            };
            router.ComputerReleased += () => microphoneTimer.Stop();
            keyboard = new VoiceTypingKeyboard(() =>
            {
                try { router.SelectComputer(); microphoneTimer.Start(); SetStatus("电脑麦克风"); }
                catch (Exception ex) { Fail(ex.Message); }
            });
            search = new SearchKeyGuard();
            var menu = new ContextMenuStrip();
            menu.Items.Add("状态与设置说明", null, (_, _) => MessageBox.Show(
                $"{tray.Text}\n\n在 Win+H 面板中将输入设为 CABLE Output。\n键盘 Win+H 使用电脑麦克风；遥控器语音键使用遥控器。\n停止遥控器后线缆保持静音，键盘 Win+H 才恢复电脑麦克风。\n若 Windows 已停止聆听，请先关闭面板再重新启动。", "T1 后台语音"));
            menu.Items.Add("退出", null, (_, _) => ExitThread());
            tray.ContextMenuStrip = menu;
            _ = RunAsync();
        }
        catch
        {
            microphoneTimer.Dispose(); keyboard?.Dispose(); search?.Dispose(); router?.Dispose();
            tray.Dispose(); applicationIcon.Dispose(); uiContext.Dispose(); stop.Dispose();
            throw;
        }
    }

    private void SetStatus(string text)
    {
#if DEBUG
        tray.Text = "T1 语音（调试版）· " + text;
#else
        tray.Text = "T1 语音 · " + text;
#endif
    }
    private void Fail(string error)
    {
        if (exiting) return;
        tray.ShowBalloonTip(5000, "T1 后台语音已停止", error, ToolTipIcon.Error);
        MessageBox.Show(error, "T1 后台语音已停止");
        ExitThread();
    }

    private async Task RunAsync()
    {
        try
        {
            int retrySeconds = 5;
            while (!stop.IsCancellationRequested)
            {
                SetStatus("连接遥控器中");
                using var capture = new BleCapture(kind =>
                {
                    switch (kind)
                    {
                        case "capabilities": SetStatus("遥控器已连接"); break;
                        case "typing_requested": SetStatus("遥控器麦克风"); break;
                        case "typing_stop_required": SetStatus("遥控器已停止，线缆静音"); break;
                    }
                }, router);
                await capture.RunAsync(address, stop.Token);
                if (stop.IsCancellationRequested) break;
                if (capture.CapabilitiesConfirmed) retrySeconds = 5;
                SetStatus($"连接中断，{retrySeconds}秒后重试");
                await Task.Delay(TimeSpan.FromSeconds(retrySeconds), stop.Token);
                retrySeconds = Math.Min(retrySeconds * 2, 60);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception ex) { Fail(ex.Message); }
        finally
        {
            microphoneTimer.Dispose(); keyboard.Dispose(); search.Dispose(); router.Dispose(); tray.Dispose(); applicationIcon.Dispose();
            uiContext.Dispose(); stop.Dispose(); base.ExitThreadCore();
        }
    }

    protected override void ExitThreadCore()
    {
        if (exiting) return;
        exiting = true;
        microphoneTimer.Stop();
        keyboard.Dispose(); search.Dispose();
        stop.Cancel();
    }
}

