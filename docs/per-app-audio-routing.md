# 按应用录音设备路由验证

日期：2026-11-12。系统版本：10.0.26200.0。

## 接口区分

常见 IPolicyConfig::SetDefaultEndpoint 改变系统默认设备。
按应用路由对应 Windows.Media.Internal.AudioPolicyConfig 的
IAudioPolicyConfigFactory::SetPersistedDefaultAudioEndpoint，参数包含 PID、
eCapture/eRender 和 role。这是未公开接口，不应当作稳定官方 API。

参考：[EarTrumpet 的接口及包装](https://github.com/File-New-Project/EarTrumpet/tree/master/EarTrumpet/Interop)。
本项目使用独立的原始 ABI 探测，不引入该项目代码。

## 当前实测结果

### 手动设置后的复测：成功

用户在 Windows 音量合成器中为“Windows 功能体验包”指定 CABLE Output 后：

- 注册表 `HKCU\Software\Microsoft\Multimedia\Audio\DefaultEndpoint\c42da0c7_0`
  默认值为 `MicrosoftWindows.Client.CBS_cw5n1h2txyewy!InputApp`。
  键名不可硬编码；应按默认值中的应用身份查找。
- TextInputHost PID 15476 的 eCapture / Console、Multimedia 均成功读出 CABLE Output。
  Communications 返回成功且无指定端点。
- `dotnet run -- --app-route-probe --roundtrip` 分别将 Console 和 Multimedia
  临时切换到电脑麦克风阵列，并读回验证，两项均匹配。
- 两个 role 随后均恢复到用户原来的 CABLE Output，读回和注册表复核均匹配。
- 系统默认输入始终为“麦克风 (Realtek(R) Audio)”，未改变。

因此已证实本机支持通过该 API 修改 TextInputHost 的持久化录音偏好。
API 接收 PID，Windows 将其关联到包应用身份；这里未调用 AUMID 参数形式的 API。
此次没有直接写注册表；API 调用后的注册表变化用于交叉验证。
这还不能证明已打开的 Win+H 录音流立即迁移，仍需用真实录音会话验证。

### 手动设置前的历史结果

探测入口：`dotnet run -- --app-route-probe`。
结果：`logs/app-route-probe.json`。

- RoGetActivationFactory 能取得 IID ab3d4648-e242-459f-b02f-541c70306324。
- 对探测进程及 TextInputHost 的 render/capture、三个 role 的读取均返回
  0x80070057（E_INVALIDARG）。
- 一次针对探测进程自身的录音路由写入也返回 E_INVALIDARG。
- 未能保存 TextInputHost 的原始路由，因此未执行 TextInputHost 写入。
- 没有调用清空全部应用路由，也没有改动系统默认麦克风。

这些历史错误已不能用于判定当前调用不兼容；在应用偏好建立后，同一接口调用成功。
尚不能断言最初 E_INVALIDARG 只由“未建立应用偏好”引起。
正式音频流程尚未使用此探测接口，仍使用原有默认输入检查。

## 尚需验证

### 2026-11-12 14:33 初始化后强制 API 重写：失败

14:33:27 对已初始化的 PID 5364 强制写入 Console、Multimedia 的
CABLE Output 偏好（没有跳过同值写入），均成功读回。
14:33:39.485 实际录音会话仍位于电脑麦克风阵列。
`scripts/Test-RouteCapture.ps1` 对本轮记录返回 FAIL、退出码 1。
14:33:51 起无活动录音会话；14:34:02 请求停止遥控器后收到 `00 00`
停止确认，随后会话关闭。三个 role 恢复均匹配，系统默认输入未改变。

当前可下的结论：该 Windows build 上，本项目已验证的这组应用偏好设置
无法控制 Win+H 实际使用的麦克风。持久化读回成功不能作为产品切换依据。
重建进程、补充 Communications 角色、初始化后强制重写三项均未解决。
尚未证明所有 Windows 版本或所有按应用路由机制都不支持此行为。

### 2026-11-12 14:31 Communications 角色测试：失败

保持 Console/Multimedia 的 CABLE Output 偏好，仅将 Communications 从空值
经 API 设为 CABLE Output，写入及读回成功。14:31:22.922 和 14:31:46.510
两次观察到 TextInputHost PID 5364 仍在电脑麦克风阵列上录音。
因此补充 Communications 角色未解决端点不生效的问题。
退出时三个 role 均恢复匹配，系统默认输入未改变。
会话记录已归档为 `logs/communications-test-*.json`，恢复记录为
`logs/communications-restore-*.json`。

下一轮已准备：`--route-session-test --force-route-rewrite`。
在已初始化的进程中，即使偏好值相同仍重新调用 Console/Multimedia 设置 API；
不修改 Communications，以单独验证强制重写是否生效。

### 2026-11-12 14:17 第二轮实测失败

`logs/route-session-test.json` 显示：14:16:55 第一轮 TextInputHost 使用电脑麦克风阵列，
14:17:05 已没有活动录音会话。14:17:45 已写入 CABLE Output 偏好；
14:17:59 遥控器 AUDIO_START、音频输出及 Win+H 请求均发生，
但同一 TextInputHost PID 15476 的活动录音端点仍为电脑麦克风阵列。
因此关闭面板后再次打开，不足以保证此进程采用新的应用偏好。
不能把 API 写入/读回通过当作语音输入端点切换通过。

可重复检查本次日志：`powershell -File scripts/Test-RouteCapture.ps1`，
实际结果为 FAIL、退出码 1。该检查只判定记录中的实际端点，不验证识别准确度。
下一步应依次验证重新创建 TextInputHost 进程、音频客户端生命周期、设备角色，
避免在同一次实验中同时改动多项变量。

### 2026-11-12 14:25 重建进程后仍失败

保留 CABLE Output 应用偏好，将 TextInputHost 从 PID 15476 重建为 5364，
未先执行电脑麦克风一轮。14:25:21 遥控器 AUDIO_START 和 Win+H 请求发生，
14:25:24.765 观察到新进程 5364 仍在电脑麦克风阵列端点上录音。
因此“重建进程即可使此应用偏好生效”在本次测试条件下不成立。
遥控器 AUDIO_STOP 收到 reason=0；退出恢复两项应用偏好读回匹配，
系统默认输入未改变。14:25:46 后没有活动录音会话记录。

注意：新进程初始 GetPersistedDefaultAudioEndpoint 返回 E_INVALIDARG，
测试使用注册表作为只读偏好后备，并因注册表值已匹配而跳过 API 重写。
本轮证明“保留既有偏好并重建进程”不足以生效，尚未验证
“新进程完成音频初始化后再次调用设置 API”的效果，也未覆盖 Communications role。
不能据此断言 Win+H 完全不支持按应用路由。

已增加交互验证入口：`dotnet run -- --route-session-test`。
窗口分别设置“跟随系统默认输入”和“CABLE Output”，每次写入后读回。
通过枚举各录音端点上的活动音频会话，记录实际进程、PID 和设备。
结果位于 `logs/route-session-test.json`；原始应用偏好在修改前写入
`logs/app-route-probe.json`。正常退出时检查 TextInputHost 活动录音已结束后恢复。
程序异常终止时自动恢复尚未实现，因此测试期间应使用“结束并恢复”关闭。
播放按钮使用已有录音向线缆发送数据；点击可能改变输入焦点，
推荐先完成两轮端点验证，再单独测试播放识别。

1. 核实未建立应用偏好时的返回语义与初始化流程；已建立偏好时的设置/读回已通过。
2. 确认 Win+H 真正创建录音会话的进程是否为 TextInputHost，不能仅按窗口所属进程推断。
3. 成功设置和读回后，检查真实录音会话的端点，证明识别使用了指定设备。
4. 测试已打开的语音会话是否需要重建才能切换；持久化偏好不等于实时音频流迁移。
5. 保存每个 role 的原始偏好，正常停止、断连、退出时恢复；异常终止由恢复日志处理。
6. 使用语音结束状态确认后再释放和恢复，避免持续聆听时混入另一麦克风。
