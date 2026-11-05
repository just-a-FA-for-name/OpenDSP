# OpenDSP

Windows / Android 耳机均衡器，作者 Fa。MIT 许可证，跨平台 JSON 配置。

## 当前状态：Windows 0.4-rc25 / Android 0.4 Library RC10 本地试用

**2026-10-24：按用户决定暂不公开发布，先自行使用，下一次维护再决定开源。**
新增双端当前EQ同步、手机桌面式机架、35条目标与测量拟合、理论响应分析、
图上调参与重做；PC/Android完整JSON继续互用。
最新使用方法、测试证据及回退限制见 [本地试用交接](spec/LOCAL_TRIAL_RC9.md)。
APK为 `dist/OpenDSP-Android-0.4-library-rc10.apk`，Windows为 `dist/OpenDSP-Windows-0.4-rc25`。
PC的21份预设已实际导入手机并直接列在预设页；修正记录见 [预设库补齐](spec/PRESET_LIBRARY_RC10.md)。
下面保留历史路径和验证说明；旧版阶段性结果不替代当前长期实听验收。

手机默认支持系统耳机路径，面向蓝牙/有线的路由监控、设备设置与前台后台服务；
USB独占保留为可选完整DSP路径。系统高级EQ是幅频近似，能力边界与测试见
[移动耳机与后台](spec/ANDROID_MOBILE_RC4.md)。

Android现以“应用音频处理工作台”为主界面：选择网易云/B站等音源，
开始处理、观察输入/输出电平、调EQ并输出USB。原本地播放器已移到辅助工具。
工作流和权限边界见 [Android工作台](spec/ANDROID_WORKBENCH_RC3.md)。

Android已加入免Root USB Host/libusb等时输出候选后端、PCM384k/32严格协商、
DSF64/128的DoP直送、受限播放捕获及桌面兼容完整DSP。
**三星/幻音4X已通过普通应用权限下的384k/32短时数字静音传输；音乐实听、DSD和长期稳定性仍未验收。**
APK：`dist/OpenDSP-Android-0.4-mobile-rc4.apk`。
实现与限制：[Android USB](spec/ANDROID_USB_20261023.md)。
Windows当前运行rc23，新增M/S与声场等功能见[高级EQ](spec/SPATIAL_EQ.md)。

**rc19 已加入 DSD 数值防护、完整 AI 调音方案预览/应用/撤销，以及四通路线性幅频、相位和群延迟分析。离线检查与 90 秒独立 DSD 解码通过；幻音 4X 每 20–30 秒短断的问题仍待该版本实际音乐试听，不能视为已经修好。**

最新交接：[2026-10-22](spec/HANDOFF-CURRENT-20261022.md)。
优化记录：[rc19](spec/OPTIMIZATION-20261022.md)。
AI 接入与调音流程：[Windows MCP 0.5](spec/AI_TUNING.md)。

Windows 候选包：`dist/OpenDSP-Windows-0.4-rc23.zip`，包含桌面程序和 `ai/OpenDspMcp.exe`。
本次更新没有重启音频服务、重绑 APO 或安装声卡驱动。

**原生 APO 历史基线（RC2）**：已在幻音 4X + SGOR LUNA、48 kHz / 32-bit 下通过原生 APO 音乐实听；
真实 96 kHz 格式同步与恢复、固定 48 kHz 独占所有权和多进程中转链路也有历史验证。
这些记录不等同于 rc19 的全功能硬件验收。长期稳定性、TEARDSP 侦听、蓝牙和自有虚拟入口驱动仍有边界。

剩余工作与验收条件见 [成品验收状态](spec/RELEASE_STATUS.md)。当前补充了设备格式持久备份/回退、物理 PCM 位深核对、播放中格式变更拦截和驱动待重启拦截，18 项故障及共享文件检查通过；这些修复尚未进行真实设备格式变更验收。

Windows 默认使用自研 C++ 原生 APO（MFX）：普通应用 → Windows 物理设备混音 → OpenEQ DSP → 设备驱动。此模式不使用 Steam 虚拟声卡、不做 OpenEQ 回环采集、不运行 OpenEQ 重采样器。设备原生格式可在“系统格式”中按驱动支持情况设置；EQ 曲线与系数按实际处理采样率计算。

桌面程序为 `dist/OpenDSP-Windows-0.4-rc23/OpenDSP.exe`，旁边的 `OpenEQ.Apo.dll` 必须保留。内置 .NET 8 Desktop Runtime。首次绑定物理设备需要 Windows 管理员权限。当前 DLL 是未签名开发组件，启用未签名音效需要用户明确允许；音频保护设置和原设备音效有备份，可从更多菜单恢复。正式签名、驱动包认证与完整稳定性验收尚未完成。

原生 APO 属于 Windows 共享音频路径，**不是 USB 独占或源文件 bit-perfect**。应用采样率与设备混音格式不同，Windows 仍可能转换；ASIO、WASAPI 独占、主动绕过系统音效、受保护内容及厂商硬件 offload 不承诺覆盖。已有第三方模式 APO 的设备会拒绝覆盖，尚需适配音效串联。Windows 原生蓝牙路径仍待新后端实机验收，不能沿用旧中转模式的蓝牙测试结果作为证明。

原来的可选中转路径仍保留：关闭“原生系统 EQ”后可使用已有虚拟端点 → 回环 → 自研 DSP → WASAPI USB 独占或蓝牙共享。这个模式需要虚拟播放驱动，本机使用 Steam Streaming Speakers；WDL ASRC 参数为 256 taps / 256 phases。它有额外的混音和时钟转换，不因最后输出高采样率就自动优于系统原生路径。

### Windows 功能

- AI 辅助调音：完整方案只读预览、自动余量、revision 防止覆盖手动修改、整组应用和撤销。
- AI 区间计量：中转/WASAPI链路的左右/整体 RMS、实际 DSP 增益和异常计数；不记录音频。
- 线性分析：L→L、R→L、L→R、R→R 幅频、相位与群延迟，设置页导出 JSON 或通过 MCP 读取。
- DSD 诊断：50% 调制参考余量、非有限输入防护、限幅计数及最终 DoP 标记校验；硬件短断仍待实听。

- 32 段参数 EQ、7 类滤波器、逐段旁路和左右声道选择；曲线节点拖动、滚轮调 Q。
- 频率、增益、Q、前级精确输入，按钮 0.1 步进；左右声道频响曲线。
- 31 段图示 EQ 起点、常见 Equalizer APO / AutoEQ 参数文本导入；不支持的指令明确拒绝。
- 输出增益、平衡、左右延时、反相、sample-peak 保护；自动估算前级余量。峰值保护不是 true-peak 限幅器。
- 设备原生通知加定时检查；自动识别 USB / 蓝牙、按设备保存并恢复 EQ。可关闭自动跟随新设备与自动记忆。
- 输出格式查询设备驱动支持情况，按设备记忆采样率、位深与缓冲方式；可验证自定义采样率，拒绝不支持的组合。
- 四套起步预设：平直、温暖、清晰、柔和；手动预设、A/B 槽、撤销、通用配置导入导出。
- 蓝牙自动采用共享模式，返回 USB 恢复独占偏好。
- Windows“侦听此设备”：原生 APO 直接处理物理播放路径，麦克风仍可共享采集；可选中转独占模式才临时重定向侦听到虚拟入口，并在停止后恢复。
- 托盘运行、单实例、异常路由恢复、设备失活停止与重新可用时恢复。

### Android 功能与边界

APK：`dist/OpenDSP-Android-0.4-mobile-rc4.apk`，Android 9+、arm64，目标 Android 16。应用音频捕获需Android10+。开发签名用于本地测试，不是正式发布密钥。

新增USB直连使用授权的文件描述符和原生等时传输，支持UAC1/UAC2明确列出的格式。
它与下文历史共享/Root路径分开：只有实际成功认领、设置和提交USB包的会话才是新直连路径。
Android10+播放捕获需系统和应用共同许可，无法承诺接管全部应用。
新后端仍待手机/幻音4X实测，支持范围与验收边界以顶部Android USB文档为准。

自研 PEQ、精确数值编辑、四套通用预设、配置导入导出、设备检测和设备记忆已实现。应用内 PCM WAV 播放可经完整自研 DSP 输出到蓝牙或系统选择的音频设备；该试听离开前台会停止。

另有系统原生 EQ 兼容模式，以前台服务保持后台状态并按设备记忆原生频段。**这是独立的系统原生频段，不会把通用 32 段 PEQ 近似映射成原生 EQ。** 覆盖率受系统和播放应用限制，可能无法处理 offload 等路径。Z Flip5 真机提供 5 段，0.1 dB 请求被系统量化，界面显示回读结果。免 Root 模式不能承诺所有应用获得任意完整 PEQ。

Root 全局 PEQ 的旧版已在 Android 15 / Magisk 的 Z Flip5 真机上验证：注册临时 AudioPolicy，将普通媒体与游戏混流送入完整自研 DSP，再通过 Android AudioTrack 共享输出。支持后台服务、实时更新配置与停止注销路由；不修改厂商策略文件。通话、闹钟、通知、受保护音频和所有 offload 路径不承诺接管。新免Root USB候选后端是另一条用户态等时输出路径，尚未完成真机验收，不能沿用此前共享/Root测试作为证明。整个项目尚未达到双端全功能正式成品。

### 验证范围

- 幻音 4X 原生格式：44.1、48、88.2、96、176.4、192、352.8、384 kHz 八种格式均在 AudioDG 中实测，左右 EQ 与实时切换通过。**这只是 AudioDG 内部数字响应，不是高采样率实听证明；用户实听基线是 48 kHz，96 kHz 已做真实同步+恢复验收，更高档位与频繁切换不复测轮换。**
- 原生 APO：TEARDSP 与幻音 4X 在 Windows `audiodg` 中处理 384 kHz / float32，左右独立 EQ、实时 0.1 dB 更新通过；幻音 4X 的两个独立应用进程同时播放通过。
- TEARDSP 原生 Windows 麦克风侦听：384 kHz 播放路径上信号进入 APO，同时保持共享录音；结束恢复原侦听目标与启用状态。
- 55 项 native COM / APOProcess 检查：五种采样率、七种滤波器、旁路保留 float 样本、静音、配置失效保留、控制进程心跳过期与恢复。实际终止自有测试控制进程后，Windows 音频继续，并约八秒后自动旁路 EQ。

- Tears DSP：多进程 → EQ → 48 kHz / 24-bit USB 独占，60 秒链路测试无缓冲溢出、空读或削波；数字 EQ 响应符合设定，共享竞争被独占阻止，停止后恢复默认输出。
- Tears DSP 麦克风：真实 Windows 侦听开启后仍能进入独占链路，同时可共享录音；结束恢复原来的侦听属性。
- HUAWEI FreeBuds SE 4 ANC：两个独立进程进入 EQ 并到达蓝牙播放端点，左右数字衰减符合设定，无溢出或削波；这是数字端点验证，不是蓝牙编解码或声学测量。
- 实际 USB / 蓝牙设备列表的界面切换、独立预设与持久化验证通过；模拟设备失活使用 Windows 端点禁用/恢复及真实通知，不冒充拔线测试。
- Android 15 真机：配置往返、非法配置拒绝、AudioTrack 播放、0.1 数值步进、设备标识、四套共享预设、原生 EQ 前台服务退后台保持状态通过。Android 蓝牙硬件尚待测试。
- 幻音 4X：驱动确认 44.1–384 kHz、16/24/32-bit；25 项事件缓冲与 25 项定时缓冲链路回归通过。当前版本增加全频段重采样测量，旧 64 taps / 32 phases 参数改为 256 taps / 256 phases；这仍是虚拟入口中转，可选中转模式不能等同于原生高采样率共享输出。
- Root 真机：左声道 −12.0 dB 实测 −11.992 dB，运行中改成 −6.1 dB 实测 −6.098 dB；右声道保持不变，快速重启阻挡、释放后重新启动及路由注销通过。
- Java / C# 的 7 类滤波器 × 4 采样率系数及脉冲响应一致。尚未验证整夜稳定性、所有游戏、休眠恢复和所有 DAC。

本机证据保存在忽略的 `evidence/`，不随公开源码分发。桌面预览为 WPF 自身渲染；当前 Computer Use 组件不可用，未声称完成 Windows 鼠标操作自动化验收。

## 构建

Android 当前打包包含 arm64-v8a 原生 USB 等时输出候选模块，使用 NDK 26.1.10909125；USB 实机验收尚未完成。Java 部分使用 Java 17 和 SDK `platforms/android-36`、`build-tools/36.1.0`，无需 Gradle：

```powershell
./build.ps1 -Sdk D:/adk/sdk -Jdk D:/adk/jdk17
./test-device.ps1 -Serial YOUR_ADB_SERIAL
```

Windows 使用 .NET 8 SDK：

```powershell
./windows/OpenEQ.Apo/build.ps1
dotnet publish windows/OpenEQ.Desktop -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o dist/Windows-0.4-dev
```

NAudio 固定为 2.2.1。发布时需同时附带 `LICENSE`、`THIRD_PARTY.md`、NAudio 与 .NET 的完整许可声明。Android `signing/` 必须保留在本地并备份，严禁上传私钥。源码尚未发布 GitHub，不包含虚构仓库链接。

## 配置

格式见 `spec/preset.schema.json`。配置保存滤波参数而非设备系数，系数按采样率重算；硬件路由偏好与设备绑定在各端单独保存。Windows 与 Android 自研 DSP 使用相同字段；原生系统 EQ 设置独立保存。

预设是温和试听起点，没有按具体耳机测量校准。输出增益是软件数字增益，不等于声卡或耳放硬件音量。
