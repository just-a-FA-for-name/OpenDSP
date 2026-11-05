# 当前阻塞：声音断续，暂停扩功能

最新：用户已重插，确认自动路由调整后大部分断续消失、后来偶发失真也较少，
但仍明确不能交付。已在活跃音乐快照见NetEase系统输出改为speaker/device3，
捕获非静音、USB仍384k32且underrun/errors为0，输出峰值约−5.35dBFS。
手机随后低电量充电，**暂停真机测试，不要求用户现在配合**。

RC5在电脑完成重采样相位插值修正与离线验证，增加调度/捕获/饱和诊断；
APK尚未安装，手机仍RC4。见`ANDROID_STABILITY_RC5.md`，不要将RC5离线结果
解释为用户此前实听改善的来源，也不要宣布音频问题解决。

用户明确RC4声音仍一卡一卡，对继续加功能不满。
**现在只解决声音连续性；没有用户实听确认不能写“已修复”，保持384k/32和调音参数。**

2026-10-23 20:12读取活跃UsbAudioService：
- 请求/回读384000Hz、32位；USB欠载与错误均为0。
- 队列约15k–25k帧；最大source/resample约28.7ms，DSP约11ms。
- 这次采样网易云处于paused，输入能量0，不能当作已采到卡顿片段。
- 细节在`evidence/android-stutter-live-rc4.json`。

确认存在需要排除的路由冲突：
- libusb正在认领幻音4X。
- Android AudioPolicy仍把同一4X注册为系统USB耳机输出Port100，
  网易云AudioPlaybackConfiguration也仍指定deviceId100。
- Android官方USB文档明确指出自动USB路由可能干扰USB-aware直驱应用：
  https://source.android.com/docs/core/audio/usb
- 这只是有证据的嫌疑项，尚未证明是断续的唯一原因。

已通过授权ADB调整**测试机**：
`settings put secure usb_audio_automatic_routing_disabled 1`
原值为`null`（设置不存在），记录在
`evidence/android-usb-routing-setting-before.json`。
如需还原：`settings delete secure usb_audio_automatic_routing_disabled`。
这个设置影响系统自动USB输出，不改变蓝牙路由，也不绕过播放捕获政策。
APK本身没有WRITE_SECURE_SETTINGS权限，不能将ADB测试步骤冒充应用自动能力。

设置后旧Port100仍存在，需要重插DAC才能重新应用。
已告知用户：停止处理 → 拔插4X → 重启USB捕获 → 同一片段384k/32。
**当前等待用户物理重插与播放。没有换包、没有改EQ、没有新声学结论。**
下一步核对AudioPolicy不再将4X用作系统输出，再读取有声音时的连续诊断并让用户听。
如仍断续，继续检查捕获端缺段/AudioRecord丢帧、播放器与系统时钟、实际USB包调度，
不能因USB ring underrun=0就认为无断音。
