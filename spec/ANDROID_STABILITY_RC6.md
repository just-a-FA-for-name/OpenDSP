# RC6：后台调度、提交储备与USB音量

## RC5真实现场

RC5有声观察两分钟内：USB传输错误0、软件FIFO欠载增量0、PCM硬削顶0，
但89次回调间隔超过12ms预算，最长106ms。继续停放时最长增至386ms，
软件FIFO满到0.5秒，输入仍有音乐而输出进度大幅落后。
进程PID与MediaProjection均未消失，不能把这个现场写成“低内存杀进程”。

当时RUN_ANY_IN_BACKGROUND为ignore，进程位于Samsung的abnormal cpuset/freezer组，
CPU仅0–2。通过已授权ADB将本机后台AppOp改为allow并加入Doze白名单。
原值见`evidence/android-background-settings-before.json`。
旧进程没有立刻退出abnormal；更新重建的新进程已不在该组。
这些本机设置不等于APK可以自行越过所有厂商策略。

## 修改

- USB预提交64个1ms传输，原12ms覆盖提升至64ms；启动160ms、FIFO目标96ms同步调整。
  这是稳定性储备，会增加启动/处理延迟，不降低384k/32、不改用户EQ。
- AudioRecord推迟到实际第一次读取时启动，避免USB准备/滤波器构造期间先溢出捕获缓冲。
- 极低的双精度滤波状态归零，避免静音后维持次正规数尾巴；阈值1e-30，远低可闻范围。
- 保留RC5相位插值修正，增加明确的停止原因：用户、通知、拔出、授权结束、服务销毁或管线错误。
- Service.dump导出PID、cgroup、后台限制和电池豁免状态；界面提供真实状态与用户操作的电池设置入口。
- Android收回MediaProjection许可时停止并提示重新授权，不伪装无许可恢复。

## USB PCM主音量

独立于EQ前级/输出级，默认100%保持原有增益，范围0–100%、只衰减不放大。
静音保留百分比，20ms斜坡避免音量突变；启动直接采用已保存电平。
主页面滑条/静音、通知栏音量±/静音/停止、前台音量键均接入同一状态。
后台另注册VolumeProvider媒体会话，硬键由Android选择活动会话，不能保证所有机型总选OpenDSP。
DSF/DoP不处理数字音量，仍使用DAC硬件音量，避免损坏DSD位流。

## 验证与剩余验收

离线：DSP、变时钟SRC、PCM/DSF与音量斜坡/静音/中途改目标/非法增益、静音滤波尾巴测试通过。
NDK、APK、签名和对齐检查通过。

三星上静音3秒完成1,152,000帧，无USB错误/欠载，64ms队列回读正确。
VolumeProvider设置电平、静音保持、原预设不变和结束释放通过；
当时锁屏，因此未测前台物理音量键。仅静音数据，不生成音乐或纯音。
证据：`evidence/android-usb-service-volume-rc6.json`。

用户随后用网易云播放，反馈“暂时没有断过”；只读五分钟观察写入
`evidence/rc6-live-20261023-212043.jsonl`及summary。
最终记录300.65秒、299个采样，欠载/USB错误/超64ms预算回调/PCM硬削顶增量全部0，
最大回调间隔24.768ms，结束仍在processing、384k/32且后台限制false。
这是已跨过旧两分钟问题点的一轮通过，不是长期或锁屏验收。
不能将短时无断音当作长期或所有机型验收，仍需锁屏/亮屏、跨曲和更长时间实听。

APK：`dist/OpenDSP-Android-0.4-stability-rc6.apk`，code10，600,528字节。
SHA256：`697579378B21D87567E06FFFED40CA120A0FAD22170E787B453010651FFED65A`。
