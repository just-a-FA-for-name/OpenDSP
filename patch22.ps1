$x='D:\AI workspace\OpenEQ\windows\OpenEQ.Desktop\ModernWindow.xaml'
$a=[IO.File]::ReadAllText($x)
$a=$a.Replace('共享原生模式下压缩器 / 矩阵 / 直流 / DSD 不生效，仅 EQ（APO）处理。','原生模式下 EQ / 压缩 / 矩阵 / 直流均由系统引擎内核处理；DSD 输出需开启 USB 独占。')
[IO.File]::WriteAllText($x,$a)
$c='D:\AI workspace\OpenEQ\windows\OpenEQ.Desktop\ModernWindow.xaml.cs'
$b=[IO.File]::ReadAllText($c)
$b=$b.Replace('"注意：共享原生模式仅 EQ（APO）生效；压缩器 / 矩阵 / 直流滤波 / DSD 需开启 USB 独占输出。"','"原生模式下 EQ / 压缩 / 矩阵 / 直流均由引擎内核处理；DSD 输出需开启 USB 独占输出。"')
[IO.File]::WriteAllText($c,$b)
'notes updated'
