# Voicemeeter 标准版试验模式

标准版 1.1.3.0 已安装，Remote API 双通道路由往返测试已通过。
已配置本机电脑麦克风、CABLE Output 和 Realtek 输出时钟，
配置保存到 `config/voicemeeter.xml`。驱动安装说明要求重启后使用；
重启后已验证遥控器通道到 B1 的实际音频转接，测试结果见
`logs/voicemeeter-audio-test.json`。电脑麦克风实际讲话和 Win+H 尚待交互验证。
保留原有 VB-Cable 直接模式以便对照。

## 固定路由

- Hardware Input 1 / Strip[0]：电脑的真实麦克风。
- Hardware Input 2 / Strip[1]：CABLE Output (VB-Audio Virtual Cable)。
- 程序继续把遥控器音频写入 CABLE Input。
- Windows 默认录音输入：Voicemeeter 的 B1 输出（旧名称为 Voicemeeter Output，
  新版可能显示 Voicemeeter Out B1）。
  重启后本机名称为 **Voicemeeter Out B1 (VB-Audio Voicemeeter VAIO)**，
  已设为 Windows 默认录音设备；播放输出保持 Realtek 扬声器。
- 在 Voicemeeter 中选择可用的硬件输出 A1 作为音频引擎时钟，
  两个麦克风通道均关闭 A 监听，避免从扬声器播放自己的讲话。
- 平时电脑麦克风 B 开启、遥控器通道 B 关闭。两通道取消静音，增益先设 0 dB。
- 设置保存并启用 Voicemeeter 随系统启动后，由混音器维持日常麦克风通路。

## 程序使用

在已确认混音器路由和日常麦克风正常后，勾选“联动 Win+H”和
“Voicemeeter 模式”。能力确认后，点击文本框并按遥控器语音键。
程序通过 Remote API 将 Strip[0].B1 关闭、Strip[1].B1 开启；
停止遥控器录音后恢复电脑通道。先断后接以避免两个麦克风叠加。
不在此模式下切换系统默认音源。

API 路由往返测试：

```powershell
dotnet run --project T1BLE.csproj -- --voicemeeter-test
```

该测试会切换两次通道，并读取 B1 路由参数验证，最后恢复电脑麦克风路由。
结果为 `logs/voicemeeter-test.json`。测试时不要进行会议或录音。
该测试仅验证控制接口，实际音频和 Win+H 还需端到端验证。

## 已知边界

- Voicemeeter 本身必须运行；它退出时虚拟麦克风不会继续桥接。
- 正常停止、退出和错误清理会尝试恢复电脑通道；强制结束或进程崩溃时，
  尚无独立看门狗保证恢复。此时在 Voicemeeter 手动关闭第二路 B、开启第一路 B。
- 这版会控制前两个输入通道，试验前确认它们专用于本项目。
- 禁用原有 Windows“侦听此设备”转送，以免重复混音或回路。
- 当前面板启动/停止限制与直接模式一致。

## 依据

- [Voicemeeter 标准版官方页面](https://vb-audio.com/Voicemeeter/)
- [官方 Remote API 文档](https://download.vb-audio.com/Download_CABLE/VoicemeeterRemoteAPI.pdf)

文档中的 VBVMR_Login/Logout、VBVMR_SetParameterFloat、
VBVMR_GetParameterFloat、VBVMR_IsParametersDirty 用于连接与确认参数。
标准版支持 Strip[i].B1 输出总线分配。
