# 验证摘要（0.4.0）

本页是随源码公开的摘要。原始报告含真机标识与测试令牌，留在本地 `evidence/`，不随仓库分发；本页不新增任何未在真机或离线测试中实测的结论。

## 测试环境

| 项 | 值 |
| --- | --- |
| 手机 | OnePlus PKX110，Android 16 / API 36 |
| 系统状态 | user build、verified boot green、SELinux Enforcing、无 su / 无 Magisk |
| 权限 | 普通应用 uid，全程未调用 su、未修改系统设置 |
| 包 | `OpenDSP-Android-0.4.0.apk`，versionCode 17，开发签名 v3，16 KB 页对齐 |
| 桌面 | Windows，自研原生 APO 共享路径 |

## 安装与稳定性

| 检查 | 结果 |
| --- | --- |
| 全新安装 / 覆盖安装 | 均 Success；版本、21 份预设、三项运行时权限全部保留 |
| 冷启动 | 94–161 ms |
| 崩溃 / ANR | 零 FATAL、零 ANR |
| 全新安装后权限 | 通知 / 蓝牙 / 录音三项默认未授予，需用户手动允许；授权后冷启 94 ms 无异常 |
| Doze 白名单 | 测试机未加入，属厂商后台限制风险点，未擅自修改 |

## 八项仪器测试

| 分支 | 结果 | 关键数据 |
| --- | --- | --- |
| `no_audio` | PASS | 17 项 case：JNI/libusb、20 份预设往返、384k DSP、5 页面渲染、机架编辑、撤销、无 DAC 保护 |
| `workbench_layout` | PASS | 捕获优先层级、指定应用可见性 |
| `headphones` | PASS | 64 频段系统 EQ 有控制权，1 kHz / +3 dB 回读 2.9977 dB，后台服务保持 |
| `native_src` | PASS | 样本误差 1.39e-16；原生 SRC 71.9 ms vs Java 91.0 ms |
| `preset_roundtrip` | PASS | PC 夹具往返一致 |
| `import_pc_library` | PASS | 21 份参数精确一致、幂等、不改当前调音 |
| `mobile_features` | PASS | 7 页面、图上拖动撤销重做、真实网络双向同步、后台服务、冲突保留双方 |
| `volume_curve` | PASS | 1% = −51.98 dB、2% = −49.98 dB、50% = −6.02 dB、100% = 0.00 dB；全刻度单调、单次按键精确 1% |

`usb_prepare` 因测试机未插 OTG DAC 报 `Expected ACMEE 4X`，属环境缺口，**不计通过**。

## 定版包与已验证基线的关系

`0.4.0` 是在真机验证通过的 RC12 基础上升版本元数据（code 16 → 17、versionName → `0.4.0`），未改动任何功能代码。对两个 APK 逐条目 SHA-256 比对：

- 34 个条目，无增删；
- 仅 `AndroidManifest.xml` 与三处 v1 签名元数据随版本变化而改变；
- **其余 30 个条目（classes.dex、全部 arm64 原生库、resources.arsc、assets）逐字节一致**。

签名同为本地 debug 密钥 v3 方案，zipalign 16 KB 校验通过。因此 RC12 的真机功能验证结论直接继承；定版包本身未再次覆盖安装（构建时设备已断开）。

## 仍未关闭

插 DAC 后的 USB 认领 / 384k32 等时传输 / DoP 实听、网易云或 B 站实际捕获与长时实听、真实蓝牙切换、OEM 后台长时（本机未进 Doze 白名单）、DSD64/128、硬键与通知按钮在实际处理中的拦截路径、正式签名与安装器。

这些不是"小问题清单"，是明确的能力边界。宣传与说明中不得把本项目描述为"全部应用强制接管"或"bit-perfect"。
