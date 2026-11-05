# OpenEQ.VirtualCable — 自有虚拟音频入口驱动（开发中）

「全应用 USB 独占」链路的缺失环节：一个可分发的虚拟播放端点。本目录是它的驱动源码与构建产物。

## 状态（2026-10-17）

- **已打通无管理员权限的完整构建**：`build-driver.ps1` 用 WDK NuGet（Microsoft.Windows.WDK.x64 10.0.26100.1882）+ 本地 MSBuild 工具集垫片（`build/research/wdk-nuget/`）产出并测试签名：
  `x64\Win10\Scream\OpenEQ.VirtualCable.sys` + `OpenEQ.VirtualCable.inf` + `scream.cat`。
- **未加载**：装入系统需要 test-signing（`bcdedit /set testsigning on` + 重启 + 关闭 Secure Boot），这是用户决策，脚本不会代做；见 `install-testsigning.ps1`。
- 正式发布不能用 test-signing：需 EV 证书 + 微软 Attestation/HLK 签名（见 `spec/USB_EXCLUSIVE_DECISION.md`）。

## 来源与许可

驱动源码衍生自 Scream（github.com/duncanthrax/scream，**MS-PL**，源自微软 MSVAD 示例）。MS-PL 全文见 `MS-PL-LICENSE.txt`；衍生部分须保留原版权声明。设备名已改为 “OpenEQ Virtual Cable”、二进制为 `OpenEQ.VirtualCable.sys`；内部标识沿用原项目（根枚举硬件 ID `*Scream`、服务名 Scream），源码内部符号未改名。

## 与 OpenEQ 的集成（已就绪）

- OpenEQ 的 `DeviceTransport.Detect` 按名称把含 “Virtual/CABLE/虚拟” 的设备识别为虚拟入口，本设备名自动命中。
- 中转模式（关闭“原生系统 EQ”）把该设备选作“系统音频入口”，即可走：全应用 → OpenEQ Virtual Cable → WASAPI 回环采集 → 自研 DSP → USB 独占输出。现有 `AudioEngine` 无需改动。
- 边界照旧：多源混音统一输出时钟后重采样，不承诺每源 bit-perfect；ASIO/WASAPI 独占直连设备的应用不经过此入口。

## 构建与安装

```powershell
# 构建（普通权限）
.\build-driver.ps1

# 安装（管理员；test-signing 已开启时才会真正安装）
.\install-testsigning.ps1
```

构建链要点（均在 `build/research/wdk-nuget/vctargets/Platforms/x64/PlatformToolsets/WindowsKernelModeDriver10.0/`）：
`Toolset.props/targets` 复刻 WDK 安装器写入 VS 的工具集注册，含 kit 宏（KM_IncludePath/DDK_LIB_PATH 等）、EnterpriseWDK/WDK_NuGet 标记、混合架构工具路径（stampinf 在 x64、Inf2Cat 在 x86）、10_X64 目录版本列表。共享头（ks.h、warning.h 等）从本机 SDK 22621 补入包内 `Include\10.0.26100.0\shared`。
