# RC8：PC文件互用与实时线程降负载

用户明确：换包时中断一次；发消息时发生第二次，不能解释成升级。
已核对第二次是RC7/code11的活跃服务：无崩溃/新传输错误，但再次进入Samsung
`abnormal` cpuset/freezer，回调最大124ms、超64ms预算475次、补零12994帧。
后台AppOp=allow且Doze已豁免，说明此前设置不足以持续解除OEM调度限制。
现场：此前工具输出及`evidence/rc7-background-recurrence.txt`；
注意后者读取时服务可能已结束，不能替代已取得的实时快照。

## PC/Android共用文件

通用`org.openeq.preset`/version1 JSON保持不变，新增手机严格APO子集导入，
使用与PC相同的语法和范围；导出仍为完整通用JSON。
不把蓝牙系统路径能力差异写进预设或丢弃USB/PC空间参数。

已实际执行：
1. PC真实Preset.Encode生成32段、左右独立、全部输出/压缩/矩阵、
   Mid/Side各16段、空间与设备提示的完整预设。
2. Android实际JSONObject解析、导出，不应用这些测试参数。
3. 拉回PC，以真实Preset.Parse读取并按规范化JSON逐字段比较，完全一致。
4. PC与Android的APO导入结果返回PC同样完全一致。

工具/夹具：`tests/PresetInteropTest.java`及`build/stability-rc8/interop`。
PC与手机预设**可以双向直接互用**；不能据此宣称手机所有路由都支持文件里的所有效果。

## 负载修改

- 重采样64抽头、相邻相位插值保持数学目标不变，将内层移入C++，arm64用立体声NEON，
  JNI按块调用，不按样本跨语言。DSP仍在目标384k处理，没有偷偷降成48k EQ。
- 384k同率本地文件继续绕过重采样。
- PCM量化尺度用精确整数移位转double代替每样本调用ldexp。
- 保留RC7的有界错误处理、完整停止快照和取消唤醒。

本机与手机均比较Java/原生逐样本结果：
宿主误差约1.67e-16，arm64手机误差约1.39e-16。
手机纯内存3秒音频：Java重采样145.4ms CPU，原生81.0ms；当前1段DSP约72.7ms。
这只证明组件正确性与CPU时间降低，**不是整个服务CPU同比下降或卡顿已修复**。
实际USB工作仍有JNI量化、等时I/O与系统调度成本，需长期运行验证。

## 当前验证状态

- 构建、签名、zipalign、旧DSP/SRC/音量与IO回归通过。
- 真机原生SRC/Java样本对比和PC完整预设往返通过，不播放音频。
- RC8已覆盖安装。当前未开始新的用户内容捕获测试。
- 静音USB回归尚未运行：测试准备时DAC未取得授权/未在应用内可用，
  返回`DAC must already be authorized`，不能计为传输通过或音频算法失败。
- 反复后台限流未宣称解决。不要继续要求用户边卡边重复试听，
  先保留现场与明确测试路径，必要时请用户连接/授权DAC。

后续更新：用户已插回4X，确认RC8的应用捕获会话已启动并实际使用native_stereo_sinc64。
约222秒等待音源的数字静音快照零错误/欠载/计损/超预算。没有重启该会话。
已请用户播放熟悉内容并启动15分钟只读观察，结果待记录，不能将静音运行当作音乐验收。

APK：`dist/OpenDSP-Android-0.4-stability-rc8.apk`，code12，612816字节。
SHA256：`0F2DA2CBAF23D766AF2AE5C3A7E84D82D7141B011FB2C0A67687677C6743D951`。
