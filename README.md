# OpenDSP

**不是给耳机加个 EQ，是把整套调音台还给你。**

Windows + Android 的耳机均衡器与 DSP 工作台。自研原生 APO、免 Root USB 独占、双端共用一份预设 JSON。全部本地运行，没有账号、没有云、没有遥测。MIT 协议，作者 Fa。

> 当前版本 **0.4.0**（2026-11-05 定版）。开发签名，自用与尝鲜阶段；正式签名发行版、长时实听与部分硬件路径仍未关闭。**说得清边界，比多喊两句口号重要。**

---

## 为什么会有这个东西

- 系统给的那十段 EQ，不够用；而且不一定管得到你正在听的那个 App。
- 手机插上小尾巴，音量开到 2% 还是炸耳朵 —— 低阻高敏耳机根本没有低端余量。
- 电脑上调好的预设，手机上得重来一遍。
- 想要完整 DSP？往往意味着 Root、装虚拟声卡、改驱动，或者干脆换一套封闭生态。

OpenDSP 的答案很直接：**把电脑上的完整 DSP 搬到手机，免 Root、不装虚拟声卡、不改系统设置；预设就一份 JSON，两端通用。**

---

## 它能干什么

| | Windows | Android |
| --- | --- | --- |
| 主路径 | 自研 C++ 原生 APO（系统共享音频路径），不需要虚拟声卡 | 免 Root 的 USB Host / libusb 等时输出，普通应用权限即可 |
| EQ | 32 段参量 + 31 段图示、7 类滤波器、逐段旁路、左右声道独立 | 同款 DSP、图上拖动调参、撤销 / 重做 |
| 进阶 | M/S、声场、压缩、矩阵、延时、反相、目标曲线拟合、四通路线性相位 / 群延迟分析 | 桌面式机架、35 条目标曲线、响应分析 |
| 输出 | 原生 APO 或可选 WASAPI USB 独占 / DSD DoP 直送 | PCM 最高 384 kHz / 32-bit 严格协商，DSD DoP 直送；系统耳机路径另有 64 频段近似 |
| 预设 | 21 份内置，A/B 槽、通用 JSON 导入导出、按设备记忆 | 同一份 JSON，双端双向同步，冲突保留双方 |
| 音量 | 软件数字增益，峰值保护 | **1% 一档**，低段约 2 dB/档，100% 保持原最大输出 |

链路一句话：

```
Windows : 应用 → Windows 混音 → 自研原生 APO → 物理设备
Android : 应用（系统允许的播放捕获）→ 完整 DSP → USB 独占 → DAC
耳机路径: 系统 DynamicsProcessing（幅频近似，非完整 PEQ）
```

---

## 三分钟上手

**Windows**：从 Releases 下载 `OpenDSP-Windows-0.4.0-single.zip` → 解压到任意文件夹 → 双击 `OpenDSP.exe`。自带 .NET 8，不用装运行时。`OpenEQ.Apo.dll`、`Assets`、`LICENSE.txt` 必须和 exe 同目录。首次绑定设备会要管理员权限；不想用了删掉文件夹即可，不写注册表。

**Android**：下载 `OpenDSP-Android-0.4.0.apk` 直接安装，Android 9+ / arm64，**不需要 Root、不改系统设置**。首次运行按需授予通知 / 蓝牙 / 录音权限。低阻耳机请从 **1–5%** 开始往上找音量。

完整说明见 [`docs/使用说明.md`](docs/使用说明.md)（Release 里也附一份）。

### ⚠️ 384 kHz 不是所有应用都吃得下

**有些应用，特别是游戏，在 384 kHz 输出格式下会没声音、爆音、卡顿或报设备错误。把它切到 192 kHz 就能正常使用。**

在软件「设备」页把 **采样率** 选成 `192000`（或输入后回车让程序查询驱动），位深保持 `32-bit`，点 **「同步到系统」**，然后**重开那个游戏**。开着「USB 独占」时不能同步系统格式，先关掉；其他程序正在放声音时软件会拒绝改格式。原格式会自动备份，可在「更多」菜单恢复。

也可以在 Windows 声音设置 → 输出设备 → 设备属性 → 高级 → 默认格式里改。

**这不是降音质**：你听的音乐绝大多数是 44.1 / 48 kHz，192 kHz 仍是四倍，EQ 系数按实际处理采样率重算，功能一个不少。它只是个兼容性开关。

---

## 真实验证，不是 PPT

- 非 Root 一加 PKX110 / Android 16 / SELinux Enforcing：全新安装 + 覆盖安装通过，21 份预设与权限全程保留，冷启 94–161 ms，零 FATAL / 零 ANR。
- 八项仪器测试通过：DSP/JNI、UI 工作台、64 频段系统 EQ（1 kHz +3 dB 回读 2.9977 dB）、原生 SRC（误差 1.39e-16）、预设往返、PC 库导入、真实网络双向同步、音量曲线。
- 音量曲线真机回读：1% = −51.98 dB、2% = −49.98 dB、100% = 精确 1.0；全刻度单调，20% 处与原线性段无缝衔接。

摘要见 [`spec/VALIDATION-SUMMARY.md`](spec/VALIDATION-SUMMARY.md)。完整原始报告含真机标识，留在本地 `evidence/`，不随源码分发。

---

## 不吹的部分

- **不承诺「全应用强制接管」**：Android 播放捕获受系统与应用自身许可限制，offload、受保护内容、部分应用可能不在路径内。
- **不吹 bit-perfect**：输出 384 kHz 不代表音源是 384 kHz；系统共享路径仍可能由 Windows 做格式转换。
- **不吹蓝牙无损**：蓝牙采样率与编码由系统 / 耳机协商。
- **系统耳机路径是幅频近似**：空间、延时、反相、自研压缩不在这条路径生效。
- **仍未关闭**：无 DAC 时的 USB 出声链路复测、长时 / 整夜实听、真实蓝牙切换、DSD64/128 实听、部分厂商后台限制、正式签名与安装器。
- 当前 APK / APO 是**开发签名**；`signing/` 私钥不随仓库分发，请勿把私钥提交进来。

---

## 从源码构建

Android（无需 Gradle；NDK 26.1.10909125、JDK 17、SDK platform android-36 / build-tools 36.1.0）：

```powershell
./build.ps1 -Sdk D:/adk/sdk -Jdk D:/adk/jdk17
./test-device.ps1 -Serial YOUR_ADB_SERIAL
```

首次构建会自己生成 `signing/debug.jks`（已 gitignore）。Windows 用 .NET 8 SDK：

```powershell
./windows/OpenEQ.Apo/build.ps1
dotnet publish windows/OpenEQ.Desktop -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o dist/Windows-0.4-dev
```

NAudio 固定 2.2.1。发布时须一并附带 `LICENSE`、`THIRD_PARTY.md` 及 NAudio / .NET 完整许可声明。配置格式见 [`spec/preset.schema.json`](spec/preset.schema.json) —— 保存的是滤波参数，系数按实际采样率重算。

---

## 仓库里有什么

| 路径 | 内容 |
| --- | --- |
| `core/` | 跨平台 DSP（Java） |
| `android/` | Android 应用与原生 USB 后端 |
| `windows/` | WPF 桌面端、C++ APO、虚拟声卡驱动实验 |
| `presets/` | 预设与调音记录 |
| `spec/` | 架构、USB 决策、验证摘要、开发日志 |
| `tests/` | DSP / 音频 IO / 真机仪器测试 |
| `docs/` | 使用说明（与 Release 内同一份）、[第三方组件与许可](docs/第三方组件与许可.md) |
| `THIRD_PARTY.md` | 完整第三方声明；随产物分发的全部是开源组件 |
| `dist/` | 本地构建产物（不随仓库分发，走 Releases） |

历史阶段性记录见 [`spec/README-DEV-HISTORY-20261024.md`](spec/README-DEV-HISTORY-20261024.md)；其中版本号与"已完成"字样只代表当时状态，不能覆盖本页。

---

MIT License · Copyright (c) 2026 Fa · 第三方声明 `THIRD_PARTY.md`
