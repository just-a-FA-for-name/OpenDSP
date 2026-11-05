# 原生模式完整 DSP 机架 · 施工蓝图（零阉割）

## 纪律（用户红线，写死）
- 不做任何系统注册/音频服务重启/弹窗实验。开发全程离线，仅离线数字测试。
- 不花钱、不装私有驱动、不动 Secure Boot。
- 算法借开源：DSPFilters(BSD) 双二阶/EQ；Signalsmith(MIT) 限幅/整形；压缩/矩阵参考开源 dynamics 实现后自研重写（许可证干净）。

## 目标
apo.cpp 的 Bank::process 扩展为全机架，与 Dsp.cs StereoProcessor 逐项等价（双精度）：
1. 直流滤波 20Hz 高通（一阶/二阶，同 Dsp.cs）
2. 输入增益 + 立体声宽度（M/S）+ 矩阵 LL/LR/RL/RR
3. 软拐点压缩器（立体声联动：阈值/压缩比/启动/释放/拐点/补偿，同 CompConfig）
4. EQ 多段双二阶（已有，保留 bank 交叉淡入）
5. 输出级：平衡、左右延时、左右反相、输出增益、sample-peak 保护（Ceiling）

## 数据通路（沿用现有，不新造）
- Shared 内存 Config（seqlock）已存在：扩展字段后 version=1→2，NativeApo.cs 写端同步扩展。
- Config 增补：CompConfig 全字段、MatrixConfig 全字段、Width、DcBlock、Delay L/R、Invert L/R、Ceiling、Protection、Output.Gain、Balance。
- apo.cpp `valid()` 校验同步扩展。

## 验收（离线，全绿才上机）
- tests：与 Dsp.cs StereoProcessor 逐样本对照（同输入 1s 扫频+音乐段，误差 < 1e-9）
- 传递测试：EQ 频点 ±0.25dB；压缩 GR 曲线对齐；矩阵/宽度/延时/反相逐项
- 现有 --dsp-rack-checks / --relay-transfer-checks 全过

## 上机（一次性，验收制）
- 仅在物理设备启用原生模式（APO 已实证可锁定）；虚拟入口永不注册（规范）。
- UI 文案更新：原生模式下机架旋钮全部可用（删除“仅 EQ 生效”的说明）。
