# 可选独立硬件采集 / Optional independent hardware collection

当前支持两种模式。已有 TrafficMonitor 的用户可按 [游戏监控指南](../../docs/GAMING.md) 使用 CPU/GPU 统一桥接；此模式不启动本目录的独立采集器。FPS 在两种模式下均由 PresentMon 提供。

Users already running TrafficMonitor can use the unified CPU/GPU bridge described in the game guide. That mode starts no independent collector. PresentMon supplies FPS in either mode.

## 构建 / Build

将 LibreHardwareMonitorLib.dll 及其所需依赖放入源码 `Telemetry/` 后运行本目录的 `Build.ps1`，或项目总构建入口。已验证硬件库版本和哈希见 [provenance.json](../../WindowsTouchBar-master/WindowsTouchBar/Telemetry/provenance.json)。.NET Framework 4.x 编译器构建采集器，避免直接在 .NET 6 中加载旧驱动封装。

Build with the .NET Framework 4.x compiler after providing LibreHardwareMonitor and its dependencies in the application's `Telemetry/` directory. Versions/hashes are recorded in the provenance manifest. Without the optional library, the script still builds the dependency-free GUI sensor host for offline checks and skips the hardware collector. Building never installs or starts it.

## 数据与权限 / Metrics and permissions

CPU 温度优先使用原生 `Core Average`，缺少时只对同一次采样中有效的 `CPU Core #N` 求平均；不使用 Package/Core Max，不做时间平均。CPU 频率是有效 Core Clock 的平均。GPU 使用 Core 温度、Core 占用（缺少时 D3D 3D）与 Core Clock。注意 CPU 频率口径不同于 TrafficMonitor 主程序的性能计数器值。

CPU temperature uses native Core Average or the arithmetic mean of valid CPU core temperatures from the same sample; never Package/Core Max or a moving average. CPU clock is the mean of valid Core Clocks. GPU uses Core temperature, Core load (D3D 3D fallback) and Core Clock. CPU clock semantics differ from TrafficMonitor's host performance counter.

独立普通采集器仅在游戏页激活时尝试读取可用硬件数据。CPU 温度需要管理员驱动访问时，`Enable-SessionSensors.ps1` 提权启动单次会话助手，随绑定的 Touch Bar 退出。单向命名管道只允许当前用户读取，不接收控制命令。缺失或超过 3 秒的数据不显示。

The ordinary collector runs during game monitoring. If CPU temperature requires elevated driver access, `Enable-SessionSensors.ps1` starts a separate elevated session collector tied to the Touch Bar process. Its current-user-only pipe carries data, not commands; missing or older-than-three-second samples are unavailable.

## 自动运行 / Automatic operation

`Configure-Autostart.ps1 -Mode Enable` 需要一次 UAC 授权，将助手与配置安装到受保护的 `%ProgramFiles%/TouchBarSensors/<SID>`，创建管理员登录任务。GUI 监护等待同会话、指定完整路径的 Touch Bar，静默启动固定采集器；主程序保持普通权限。采集器跟踪主程序及监护句柄退出。重编译后重新 Enable 才会更新受保护安装。

The optional installer requires one UAC approval, installs protected files and configuration, and registers an elevated login task. Its GUI host waits for the exact application path in the same session and silently starts the fixed collector. The main app stays unelevated. Re-run Enable to update the protected installation after rebuilding.

`Configure-Autostart.ps1 -Mode Disable` 删除独立采集任务并通知监护退出，保留已安装文件。使用统一桥接时应关闭此任务，以免重复硬件采集。切回独立模式时删除运行目录 `Telemetry/cpu-source.txt`、重新启用所需采集方式并重启 Touch Bar。

Disable removes the collector task and asks its host to exit while retaining installed files. Keep it disabled in bridge mode to avoid duplicate polling. To return to independent collection, remove the runtime marker, enable the desired collector mode and restart Touch Bar.
