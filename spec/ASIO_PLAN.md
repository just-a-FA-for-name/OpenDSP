# ASIO 输出通道施工图（2026-10-19 夜 · 驱动安装待重启，代码先行）

## 背景与动机（本轮实测结论）
- 4X 的 DSD 模式可达（rc18+黄金锁位流后面板亮 DSD），但周期卡顿定位到芯片/USB 层：
  软件欠载 0、HUB 洗清、独立传输同卡、DSD128 更糟（数据翻倍）→ “芯片缓存爆”成立度最高。
- 官方 ASIO 驱动（SaviAudioBravoHD，MSI 已验签）= 厂商自己的传输节奏，可能绕开缓存问题。
- **驱动安装需重启，当前不可重启 → 代码先写好，重启后即测。**

## 实现范围
1. `AsioOut.cs`：ASIO 输出后端（NAudio.Asio 可用；NAudio 包含 AsioOut）。
   - 精确格式协商：优先 176400/24（DoP64 载体）与 352800/24（DoP128）；回退列表如实标注。
   - ASIO 声卡线程回调零分配（复用 StrictExclusiveOut 的队列/统计设计）。
   - MMCSS/优先级沿用现有严格后端做法。
2. **ASIO DSD 传输**：对照 SaviAudio ASIO SDK 头（asiosys.h 的 ASIOSTSD* 格式 ID，0x100 系魔数）；
   DSD64=16bit/样本、DSD128=8bit 等布局按 SDK 定义；DoP 载体继续走 PCM 通道作对照。
   注意：ASIO 的 DSD 用数字格式 ID，二进制无 "DSD" 字符串——此前的字符串扫描结论无效，已作废。
3. 控制接口：`set_output_backend`（wasapi|asio）+ `set_dsd` 在 ASIO 后端下的语义；
   与 WASAPI 路径互斥切换（先 stop 再切），失败自动回滚（沿用 set_dsd 的回滚模式）。
4. UI：输出后端选择（WASAPI 独占 / ASIO），状态栏如实显示实际格式与后端。

## 验收门槛（离线全绿才上机）
- `--asio-checks`：格式协商矩阵、回调零分配、队列回绕一致性、模式切换回滚、故障注入。
- 与 StrictExclusiveOut 对等的欠载/回调耗时统计。
- 上机首测顺序：ASIO+PCM176400/32 对照（应稳）→ ASIO+DSD64 → 听感/面板 DSD 灯。

## 纪律
- 驱动安装/重启等系统动作先征得用户同意；测试从低音量开始；不放尖锐纯音。
- 本文件所述均为代码层工作；未获同意不落地部署。
