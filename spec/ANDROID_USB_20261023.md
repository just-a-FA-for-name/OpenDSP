# Android免Root USB直连候选版

版本：`0.4-usb-rc2`，包名保留`org.openeq.app`、versionCode=6，应用显示名OpenDSP。
Android9+、arm64-v8a，沿用本机开发签名。
APK：`dist/OpenDSP-Android-0.4-usb-rc2.apk`。

**三星SM-F7310 + 幻音4X已通过界面检查、384k/32格式回读及短时数字静音传输。
实际音乐、长时稳定、DSD和其他手机仍待验证，不能将静音传输计数冒充实听结论。**

## RC2实机修复与结果

RC1真实打开DAC时报`claim control: LIBUSB_ERROR_BUSY`。安卓层已经通过
`UsbDeviceConnection.claimInterface(force=true)`认领同一USB文件描述符的接口，
原生层再用libusb自动detach会执行`DISCONNECT_CLAIM_EXCEPT_DRIVER("usbfs")`，
把本应用的已有认领也判为忙。已关闭这次重复自动detach，正常注册libusb接口所有权。

三星SM-F7310（Android15）/幻音4X（VID9770、PID6687）：
- 已授权的普通应用UID10322；SELinux Enforcing；未调用su。
- UAC2/high-speed，32-bit alternate3，OUT端点3、反馈端点132、最大包400字节。
- 384000Hz、32有效位/32位容器设置与回读通过。
- 原生3秒数字静音完成1,152,000帧，24,328个包、3,040个反馈包，错误/欠载为0。
- 完整文件→DSP→USB前台服务也通过3秒静音、退后台、排空及释放。
- 所有主页面和高级弹窗、参数保存/撤销已检查。早期测试的窗口同步/返回键注入问题已修正；
  未将测试脚本问题冒充应用崩溃。

设备原有Magisk，本轮不使用其权限；仍需在未Root主力机和其他内核上确认兼容性。
证据：`android-samsung-no-audio-20261023.json`、`android-usb-prepare-rc2-20261023.json`、
`android-usb-silence-rc2-20261023.json`、`android-usb-service-silence-rc2-20261023.json`。

## 免Root为什么可以

用户在Android USB Host授权弹窗中同意后，应用取得UsbDeviceConnection的文件描述符，
由libusb_wrap_sys_device接入原生USB后端，通过用户态等时传输发送音频。
它不经AudioTrack播放输出，不需要su，也不修改系统音频策略或安装Root模块。
具体内核、SELinux、USB Host控制器、供电及DAC实现仍会影响可用性。

参考：
- https://developer.android.com/develop/connectivity/usb/host
- https://github.com/libusb/libusb/wiki/Android
- https://source.android.com/docs/core/audio/usb
- https://developer.android.com/media/platform/av-capture

免Root USB输出只意味着应用可独占直接驱动DAC，**不意味着能强制接管所有其他应用的声音**。
已加入Android10+的AudioPlaybackCapture实验入口，需录音权限、MediaProjection授权和前台服务。
仅捕获系统及来源应用允许的媒体/游戏播放，排除本应用；不录制屏幕、不使用麦克风。
受保护、禁止捕获或不满足系统条件的音频可能为静音，不能绕过。
该API也不能替其他应用静音或强制重定向，系统端可能同时发声。

## 三条音频路径

1. 本地WAV/系统解码器支持的FLAC、AAC、MP3等 → 必要时sinc转换 → 完整DSP → 原生USB PCM。
   WAV支持16/24/32-bit整数及32/64-bit浮点，原生32-bit整数输入保留到double处理。
   MediaCodec回读实际输出格式，不承诺每个手机的解码精度或格式覆盖。
2. 双声道DSF DSD64/128 → DoP176.4/352.8kHz → USB24/32-bit载体。
   此路径绕过全部DSP、软件增益和压缩，DSF位序转换及DoP标记由代码控制。
   暂无手机PCM实时转DSD、DSD经DSP后重新调制、DFF或厂商原生DSD协议。
3. 获准应用播放捕获（当前48kHz float）→ DSP → USB目标格式。
   输出384kHz不会恢复音源/系统混音已经丢失的信息。用缓冲反馈做小范围异步时钟校正。

USB PCM默认请求384kHz、32有效位/32位容器。必须出现在DAC声明能力中，并成功设置与回读采样率；
不支持时明确拒绝，不自动降为48kHz或改用共享AudioTrack。
DoP需用户选择对应模式，且DAC真正支持DoP；PCM描述符本身不能证明DSD支持。

## 后端实现

- UAC1/UAC2、双声道PCM、full/high speed；同步、自适应、带显式反馈的异步输出。
- UAC2时钟源/选择器解析、采样率范围查询、SET/GET回读、时钟有效性检查。
- 原生传输池、分数采样包长、3/4字节反馈解析、反馈异常/失活检测、包错误停止。
- 有界音频队列、启动预缓冲、背压、EOF排空、取消/拔出清理，尽量恢复原采样率与内核接口。
- PCM欠载补零；DoP欠载填入DSD静音字并保持05/FA标记连续，同时记录欠载。
- 接口认领、协商格式、帧数、反馈、欠载、错误可复制诊断，停止后保存到应用私有目录。
- 不支持隐式反馈、UAC3、多音频控制功能、非双声道或厂商私有音频协议；明确拒绝。
- 驱动异常导致原生取消迟迟未完成时，不能冒险释放仍被USB回调引用的内存；
  该类手机/内核异常需实机确认，不能承诺自动恢复所有故障。

## DSP与界面

手机端已补齐桌面配置中的压缩器、矩阵/宽度、DC和M/S空间块，
包括Mid/Side各16段、分区宽度、交叉馈送、混合及单声道设置。
保留主32段EQ、左右选择、前级、延时、反相、峰值保护和A/B/撤销。
JSON导入/导出保留完整输出块；额外提供手机高级参数与M/S编辑、默认值和场景。
附带16份Luna预设，前级0dB。

异率转换当前为64抽头/256相位windowed-sinc，同率文件直接绕过重采样。
Android系统共享试听与Root兼容路径仍单独存在，不能混称USB独占。
USB使用前台媒体服务和唤醒锁，Activity退后台不主动停止USB播放。
手机端尚未移植桌面的完整目标拟合工作流和独立MCP服务；可导入桌面AI调好的完整JSON。

## 已做的检查

- NDK26 arm64编译；16KB页对齐的动态libusb与原生输出库。
- 主DSP五种采样率到384kHz的系数、时域响应、延时与保护测试。
- Android完整机架与Windows rc23独立执行的384kHz样本逐项比较，最大误差约7.02e-15；
  包含主EQ、DC、矩阵、压缩、延时及M/S/交叉馈送。
- WAV32低有效位、浮点非法值、DSF64/128位序/EOF、sinc升采样与降采样抗混叠。
- 原生八种采样率包长累计、反馈缩放/拒绝、容量检查、PCM32与DoP24/32打包。
- APK编译、签名及zipalign检查。

尚待验证：实际音乐听感、长时接口争用/稳定性、DoP硬件识别、
休眠/频繁拔插、其他手机与MediaCodec组合、网易云捕获策略。
测试未播放真实设备纯音，桌面OpenDSP当前音频配置未被改变。

## 接下来如何实机验收

2026-10-23补充：有线ADB可用于安装、日志和界面测试。三星已通过JNI加载、
20份预设、384k完整DSP内存处理及384k FLAC解码，不播放声音。
当前测试机锁屏，UI交互仍待解锁。无线调试已经用现有热点连接成功；
电脑不必开热点。只有同一个手机USB口要接DAC时，才需改用无线ADB等独立调试通道。

1. 手机通过OTG接幻音4X；必要时使用供电可靠的USB Hub。调试连接改用无线ADB。
2. 安装候选APK，先进入USB页“接管并查询USB格式”，授权后复制能力报告。
3. 用熟悉的本地音乐选PCM384k/32，检查回读、反馈和错误计数，由用户听是否出声/短断。
4. 停止后确认释放，再逐一测试DSF64、DSF128；不要把这一步和EQ调整混测。
5. 最后才测试获准应用捕获，核对输入率、是否静音、是否系统端同时输出。

依赖libusb1.0.30采用独立动态库；原LGPL声明、完整源码在`third_party/libusb`，
`build-native.ps1`可重建替换。开发签名私钥保留本地，不随源码发布。
