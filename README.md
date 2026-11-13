# T1 BLE 后台语音输入

Windows x64 托盘程序：键盘 Win+H 使用电脑麦克风，Google TV BLE 遥控器语音键使用遥控器麦克风。
使用 C# / .NET 10 / WinForms、NAudio 和 Windows 原生 BLE GATT API。

## 使用

1. 安装 VB-Audio Virtual Cable，并提前配对遥控器。
2. Windows 系统默认输入保持为电脑实体麦克风。
3. 在 Win+H 面板内将输入设为 **CABLE Output (VB-Audio Virtual Cable)**。
4. 双击 `dist/T1BLE.exe`，程序驻留右下角通知区域；图标可能位于隐藏图标的小箭头中。
5. 唤醒遥控器，等待连接后点击目标文本框使用。右键托盘图标查看状态或退出。

EXE 不包含 .NET 和 Windows 桌面运行库。运行电脑需要安装 **.NET 10 Desktop Runtime x64**，无需安装 SDK。
下载地址：https://dotnet.microsoft.com/download/dotnet/10.0 （选择 Windows Desktop Runtime、x64）。

## 行为

- 键盘 Win+H：打开电脑麦克风，音频通过 CABLE Input 送入 Win+H，快捷键照常交给 Windows。
- 启动闲置时不打开电脑麦克风。每250毫秒检查线缆上的 TextInputHost 录音会话，结束后释放电脑麦克风；五秒内未开始录音也释放。
- 遥控器语音键：释放电脑麦克风，请求遥控器采音；收到 AUDIO_START 后转接音频并发送 Win+H。
- 再按遥控器语音键或达到60秒：停止遥控器音频，线缆保持静音。
- 遥控器收音时按键盘 Win+H：转为电脑音频，遥控器后续数据不进入线缆；遥控器采集由再次按语音键或超时结束。
- BLE 断连后按5、10、20、40、60秒逐步延长间隔重试，成功确认能力后重置；电脑麦克风路径仍可使用。
- BLE 闲置时等待通知，不做20毫秒轮询；仅收音时设置续录和超时唤醒。电脑麦克风状态检查仅在电脑采音期间启用。
- 不修改系统默认输入、不操作 TextInputHost UI 或持久化设备偏好、不增加遥控器按键防抖。
- 两个版本均不保存 WAV、ADPCM 或原始音频通知。正式版不创建日志；调试版记录连接和控制日志。
- 拦截标准浏览器搜索键以避免遥控器抢走焦点；此接口无法区分设备，其他键盘的标准搜索键也受影响。

Win+H 是切换快捷键，音频会话观察不能保证面板任意状态下均可一键开始。
从关闭的面板开始使用；若面板打开但已停止聆听，先关闭再重新开始。
切换系统默认电脑麦克风或拔插音频设备后请重启程序。

## 开发和打包

目录安排：

- `src/`：托盘、BLE、音频路由、音频解码和键盘监听等主功能源码。
- `debug/`：独立测试与调试代码，不编入正式 EXE。
- `Properties/PublishProfiles/`：单文件 EXE 打包配置。
- `docs/`：协议说明与项目文档。

需要 .NET 10 SDK。

双击根目录的 `build.cmd`，自动还原依赖并打包正式版与调试版（Windows x64 单文件，不捆绑 .NET 运行库），
完成后显示 `dist/T1BLE.exe` 的完整路径。编译前请先从托盘退出正在运行的 T1BLE。
脚本结束时保留窗口便于查看结果；命令行或自动化调用可使用 `build.cmd --no-pause`。

```powershell
dotnet build T1BLE.csproj
dotnet run --project T1BLE.csproj
dotnet run --project T1BLE.csproj -- --address 88:BF:7A:4F:E5:42
dotnet publish T1BLE.csproj -c Release -p:PublishProfile=SingleFile
dotnet publish T1BLE.csproj -c Debug -p:PublishProfile=SingleFile -p:PublishDir=dist/debug/
```

正式版：`dist/T1BLE.exe`，不执行日志格式化、文件创建或写入。
调试版：`dist/debug/T1BLE.exe`，托盘悬停文字标明“调试版”，日志写在 EXE 旁的 `logs/`；从项目根目录启动时写入根目录 `logs/`。
两个版本共用单实例限制，使用调试版前先退出正式版。
`dist/` 中的 EXE 和日志均不提交到 Git。从源码获取项目后，运行 `build.cmd` 生成两个版本。
需要分发 EXE 时，可作为 GitHub Release 附件提供。

## 独立测试

测试代码位于 `debug/`，不编入正式 EXE。测试链接 `src/` 中的实现源文件，运行相同的音频和键盘逻辑。
先退出托盘程序并关闭 Win+H 面板，再运行：

```powershell
dotnet run --project debug/T1BLE.Tests.csproj -- --microphone-idle-test
dotnet run --project debug/T1BLE.Tests.csproj -- --source-selection-test
dotnet run --project debug/T1BLE.Tests.csproj -- --search-guard-test
dotnet run --project debug/T1BLE.Tests.csproj -- --background-audio-test
dotnet run --project debug/T1BLE.Tests.csproj -- --audio-test
dotnet run --project debug/T1BLE.Tests.csproj -- --decoder-test
dotnet run --project debug/T1BLE.Tests.csproj -c Release -- --logging-mode-test
dotnet run --project debug/T1BLE.Tests.csproj -c Debug -- --logging-mode-test
dotnet run --project debug/T1BLE.Tests.csproj -c Release -- --performance-probe
```

结果写入 `logs/`。音频测试会短暂采集电脑麦克风或向线缆发送已知测试信号。
真实遥控器音频、Win+H 面板行为和识别效果仍需实机讲话验证。
`--performance-probe` 运行同一套托盘逻辑30秒，采集 CPU、内存、线程、句柄和消息循环响应延迟，
每500毫秒更新 `logs/performance-probe.json`，结束后自动退出。

## 协议与历史记录

采用 Google Voice over BLE 1.0：GET_CAPS、MIC_OPEN、MIC_CLOSE，录音期间每五秒 MIC_EXTEND。
支持 8/16 kHz IMA ADPCM、高半字节优先解码及 AUDIO_SYNC 状态同步。
协议记录见 `docs/voice-protocol.md`。
`docs/` 和 `.scratch/` 中的早期实验文档保留作历史参考，其中旧工具入口已移除。
