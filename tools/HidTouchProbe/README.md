# Touch Bar HID 只读探测

这个工具只针对 Apple VID 05AC、PID 8302/8600。它不发送键盘事件，不写显示画面、HID 输出/特性报告，不安装或修改驱动。

默认或 `--help` 不访问设备。`--list` 会读取 HID 描述符；`--listen` 会打开一个明确匹配的触摸集合并读取输入。在完成驱动绑定后使用。使用前先退出 WindowsTouchBar，避免同时消费设备输入。

```powershell
$probe = Join-Path (Get-Location) 'tools\HidTouchProbe\bin\Release\net6.0-windows\HidTouchProbe.exe'
& $probe --list
& $probe --listen 15
```

`--list` 输出 `MatchingCollections=1`、`DescriptorLayoutMatchesApp=true` 且 `LayoutIssues` 为空，表示恰好找到一个与当前程序假设一致的触摸集合。输出还包括解析器顺序展开后的 Values，可逐项核对。它不证明物理触摸成功。

探测期间用手在触控条不同位置点按再松开。输出包括描述符、报告数量、检测到的触摸报告、松开次数和 X 范围；不会记录应用内容。`PhysicalInputObserved=true` 表示观察到触摸及松开，不代表任何快捷键已经执行。

退出码：0=检查完成，1=读取/打开异常，2=未找到唯一匹配触摸集合，3=WindowsTouchBar 已运行，4=描述符/坐标与程序假设不一致，64=命令参数错误。没有物理触摸时报告数为零不能作为硬件故障结论。

## 与应用诊断的分工

`TouchBar-Custom\WindowsTouchBar.exe --check` 完成显示 GINF 握手、REDY/CLDR、HID 打开及首次画面 UDCL 确认后退出。它不确认物理触摸，且会更新 Touch Bar 显示。通过后再用本工具检查实际输入，然后启动应用核对按钮行为。

当发现描述符布局警告时，优先核对实际字段序列。现有应用按 FingerIdentifier 后的四个字段解释 Touch（Digitizer usage 0x33）、InRange（0x32）、X、Y；本工具会显示真实描述符，避免在未匹配时盲目发送快捷键。Touch 0x33 与 Tip Switch 0x42 是不同 usage，不应混淆。
