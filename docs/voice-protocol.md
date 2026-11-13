# T1-Remote ATVV 协议依据与实测

## 来源

- [Google Voice over BLE spec 1.0（第三方镜像）](https://wangefan.github.io/linux_kernel_driver/resources/Google_Voice_over_BLE_spec_v1.0.pdf)
  文档署名 Google 工程师，日期 2020-07-11，状态 Published；页面带 Google Confidential 标记。
  此链接不是 Google 官方托管地址，不能据此断言该规范已由 Google 公开发布。
- [公开实现的协议记录](https://github.com/sebegerritsen/atv-remote-dictate/blob/main/docs/PROTOCOL.md)
  针对 Chromecast Remote 的独立实测，仅用作交叉验证，不复制其实现。

## 接口及消息

- 服务：`ab5e0001-5a21-4f05-bc7d-af01f617b664`。
- TX：`ab5e0002-…`，主机向遥控器发送无响应写入命令。
- AUDIO：`ab5e0003-…`，无头部连续 IMA ADPCM 音频通知。
- CTL：`ab5e0004-…`，控制通知。
- GET_CAPS：`0A 01 00 00 03 00`，协商协议 1.0 与 On-request 模式。
- START_SEARCH：`08`，每次语音按键触发请求。固件已防抖，程序不再增加防抖。
- MIC_OPEN：`0C 00`，实时消费模式。
- AUDIO_START：`04 <reason> <codec> <stream-id>`。
- MIC_CLOSE：`0D <stream-id>`；主机请求的流 ID 为 0。
- AUDIO_STOP：`00 <reason>`。
- AUDIO_SYNC：`0A <codec> <frame:2> <predictor:2> <step-index>`，整数为大端。
- 音频为 IMA ADPCM，先解高半字节，后解低半字节；跨通知保持状态。

## 2026-11-11 实测

日志：`logs/20261111-163342-3000863.jsonl`。
发送完整 GET_CAPS 后收到 `0B 01 00 02 00 00 80 00 00`：

- 协议版本：1.0。
- 编码能力：0x02，16 kHz / 16 bit IMA ADPCM。
- 交互模式：0x00，On-request。
- 音频帧大小：0x0080，即 128 字节。

此前五次 `08` 可确认为 START_SEARCH。
此前 `0C 01 0F` 的错误字节顺序不符合 1.0 文档中已列出的标准错误码，
仍按原始消息记录，不能直接套用 `0F 01` 的定义。

## 验证边界

能力协商、START_SEARCH → MIC_OPEN → AUDIO_START → 音频数据 →
MIC_CLOSE → AUDIO_STOP 已在 T1-Remote 上完成实测。
解码听感仍需用户试听确认；信号统计不能代替试听。
当前八秒上限是诊断录音时长，不是按键防抖，也不代表最终产品交互。

### 2026-11-11 16:47 录音验证

- 会话：`logs/20261111-164659-6244113.jsonl`。
- 按键请求：16:47:15.676；MIC_OPEN 写入成功：16:47:15.687；
  AUDIO_START：16:47:15.725。
- 音频：501 个通知，每个 128 字节，共 64128 字节 ADPCM。
- WAV：`logs/voice-20261111-164715-7222091.wav`，单声道、16 kHz、16 bit，
  128256 个采样，即 8.016 秒。
- 波形峰值 31670，RMS 2098.41，达到正负满刻度的采样数为 0。
  说明存在非静音信号且没有检测到满刻度削波，不据此保证语音可懂度。
- 16:47:23.725 发送 MIC_CLOSE（`0D 00`），
  16:47:24.175 收到 AUDIO_STOP，reason=0（主机关闭），停止得到遥控器确认。
