# OpenDSP：AI 辅助调音

Windows rc23 提供本地 MCP 0.5。沿用 `\\.\pipe\OpenDSP`，不上传音频，
不需要在 OpenDSP 内保存 AI 服务密钥。使用哪个模型，由你的 MCP 客户端决定。

## 接入

启动 OpenDSP 后，在支持 MCP stdio 的客户端添加：

```json
{
  "mcpServers": {
    "opendsp": {
      "command": "D:\\AI workspace\\OpenEQ\\dist\\OpenDSP-Windows-0.4-rc23\\ai\\OpenDspMcp.exe"
    }
  }
}
```

路径以实际解压位置为准。更新后重启客户端的 MCP 连接，重新获取工具列表。

rc23新增M/S EQ、分区宽度、交叉馈送、混合与空间场景。
工具为`list_spatial_scenes`、`set_spatial`、`apply_spatial_scene`，
重置支持`section:"spatial"`。当前USB独占链路支持，现有原生APO明确拒绝启用这些功能。
详见 [SPATIAL_EQ.md](SPATIAL_EQ.md)。

rc22新增35条曲线、测量体系校验、按实际采样率拟合的`preview_target`，
以及`list_targets`、`get_target`、`get_tuning_context`、`reset_output`。
完整操作与数据边界见 [TARGET_TUNING.md](TARGET_TUNING.md)。
目标预览默认保留前级，返回`apply_arguments`供原样应用；本用户的Luna基准为0dB。

## 推荐调音流程

1. 告诉 AI 耳机/DAC、听感问题和偏好，例如“人声有点闷，低频别再加量”。
2. `get_state` 读取当前输出链路，`get_preset` 读取完整预设与 `revision`。
3. AI 修改预设副本，通过 `preview_preset` 查看变化、自动余量、左右 EQ 曲线和提示。
4. `apply_preset` 传入预览返回的 `preset` 与 `base_revision`（字段名为 `expected_revision`）。
   所有参数一起更新，并进入撤销历史。
5. 用户试听后继续微调；不合适时调用 `undo_preset`，携带应用后返回的新 `revision`。

预览不改变声音。手动参数、设备、采样率或模式变化后，旧 revision 会失效，
避免 AI 用过时方案覆盖刚做的调整。写入请求超时后，应先重新读取预设，不能盲目重发。

`auto_headroom` 默认启用：保留用户已有的更低增益，并考虑 EQ 提升、声道矩阵、
立体声宽度和压缩器正补偿。EQ 旁路时，余量调整落在仍然生效的输出增益上。
过大的叠加提升会被拒绝；预览会说明调整后的实际参数。

旧的单段、前级、压缩器、矩阵、目标曲线等工具保留兼容。多参数方案建议使用完整预设流程，
避免逐段改动时的中间状态。

## HiFi 分析与诊断

- `analyze_response`：四条线性声道路径（L→L、R→L、L→R、R→R）的幅频、
  包裹相位和群延迟。包括 EQ、DC 阻断、矩阵、宽度、平衡、反相、实际整数采样延时。
- GUI 设置页的“导出频响 / 相位分析”可保存同一份 JSON 报告。
- 零增益路径的相位/群延迟为 `null`，不伪造无意义的数值。
- 分析不包括压缩器、限幅、ASRC、DSD、设备缓冲、DAC 和耳机声学响应；
  群延迟不是系统端到端延迟。
- `get_dsd_diagnostics`：DoP 标记、生成队列、输出欠载及调制限幅计数。
  软件统计正常不能代替幻音 4X 的实际连续试听。
- `get_audio_diagnostics`：管理式链路的异常浮点输入计数和最近错误；
  非活动/非管理式路径的相应计数返回 `null`，不是“实测零异常”。
- `measure_levels`：对当前中转/WASAPI链路进行 1–10 秒只读区间分析，返回左右及整体
  输入/输出 RMS dBFS、实际 DSP 增益、区间欠载/溢出/非法输入计数。
  不播放测试音、不录制音频。测量中换设备、改预设、重启流或计数回退会拒绝结果。
- 区间 RMS 是 DSP 前后、编码和 DoP 衰减之前的数字电平；不包含 DSD 余量、OS/DAC 音量，
  不是 LUFS 或耳机声压。附带峰值是当前 DSP 处理器的累计峰值，不能当作区间峰均比。
  原生 APO 暂无同等区间计量时会明确报不可用，静音返回 `null` 而不虚构响度。

已有近似 Harman / DF 等曲线只是目标或起点，不等于具体耳机的实测校准。
AI 不应编造测量数据、根据品牌名称宣称音质提升，或把 sample-peak 保护说成 true-peak。

## 实现与验证

- GUI 控制请求的快照、校验、提交和回退在一个 UI 调度事务中完成。
- MCP 只转发已公布工具，参数不能覆盖内部 `cmd`；支持结构化错误、通知及有时限的管道请求。
- 新工具不自动播放测试音。现有 `set_dsd` 会重开输出，只用于用户明确要求的模式切换。
- 离线检查：桌面 `--checks`、`--ai-control-checks`；MCP `--checks <报告路径>`。
