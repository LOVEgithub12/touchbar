# 游戏监控 / Game monitoring

[中文首页](../README.md) · [English home](../README.en.md) · [安装 / Installation](INSTALL.md)

游戏页在一行显示 CPU/GPU 温度、占用与频率，以及 FPS 和帧时间，保留媒体音量按钮。缺失或过期数据显示 `—`。主程序使用普通桌面权限。

The single-row game page shows CPU/GPU temperature, load and clock, plus FPS and frame time, while retaining media volume controls. Missing or stale fields show `—`. The main app runs as an ordinary desktop user.

## 添加游戏 / Add games

将 [game-monitor.example.json](../WindowsTouchBar-master/WindowsTouchBar/game-monitor.example.json) 复制到运行目录 `TouchBar-USBIP/app/game-monitor.json`，改成自己的游戏 **实际进程完整路径**，并为 `Icon` 放置自己的 PNG 图标（相对运行目录）。示例使用占位路径，不自动检测安装位置，也不分发游戏图标。重启 Touch Bar 后读取配置。

Copy the example to `TouchBar-USBIP/app/game-monitor.json`, set each game's **full executable path**, and provide a PNG at the `Icon` path relative to the runtime directory. The example contains placeholders, not discovered installations or redistributed game icons. Restart Touch Bar after editing.

```json
[
  {
    "Id": "example-game",
    "Name": "Example game",
    "Icon": "Resources/Games/example.png",
    "ExecutablePaths": ["C:/Games/Example/Game.exe"]
  }
]
```

- 按前台窗口的进程完整路径匹配，忽略大小写；进入延迟 2 秒，离开延迟 3 秒。只在后台运行不会触发游戏页。 / Matching uses the foreground process's full path, case-insensitively, with a two-second entry and three-second exit debounce. Background processes alone do not activate the page.
- 不把 `LeagueClient.exe`、`LeagueClientUx.exe` 或 `RiotClientServices.exe` 识别成游戏；LOL 应配置对局进程路径。 / Those launcher/client executables are explicitly excluded; configure the actual League match process.
- 模拟器可增加 `WindowTitleContains` 字符串数组，任一字符串匹配才显示。 / Emulators can add a `WindowTitleContains` array; at least one substring must match.
- 点击游戏图标进入临时媒体主栏，最后一次松手后空闲 4 秒返回；按住不会跳页。图标文件缺失时该入口仍可点但不显示图片。 / Tap the game icon for the temporary media bar; return after four idle seconds following release, never during a hold. A missing icon leaves a clickable but blank image area.

## CPU/GPU 统一桥接 / Unified CPU/GPU bridge

适合本来就运行 TrafficMonitor 的用户：复用其 HardwareMonitor 插件已有数据，桥接本身不轮询硬件、不加载驱动、不创建工作线程。CPU/GPU 六个字段从同一个共享内存数据包读取；FPS 仍由 PresentMon 提供。

For users already running TrafficMonitor, the bridge reuses cached HardwareMonitor data without polling hardware, loading a driver or creating a worker thread. Six CPU/GPU fields come from one shared-memory packet; FPS still comes from PresentMon.

1. 按 [桥接文档](../tools/TrafficMonitorBridge/README.md) 用 Zig 构建 x64 DLL；已验证工具链为 Zig 0.16.0。关闭 TrafficMonitor 后复制到其 `plugins` 目录。 / Build the x64 DLL with Zig as documented, then copy it into TrafficMonitor's `plugins` directory while the host is closed. The validated compiler is Zig 0.16.0.
2. 在 HardwareMonitor 配置 CPU `Core Average` 及目标 GPU 的 `GPU Core` 温度、占用和 MHz 频率项。这些项可以不显示在任务栏，但必须存在于插件配置。 / Configure CPU `Core Average` and the selected GPU's Core temperature, load and clock items. They may be hidden from the taskbar. The clock must display its MHz unit.
3. 在 TrafficMonitor **当前插件配置目录**创建 UTF-16 LE 的 `TouchBarCpuBridge.ini`，用 `[bridge]` 下的 `gpu_identifier` 明确选择适配器；例如 `/gpu-amd/0` 只是示例。更改后重启 TrafficMonitor。 / Create a UTF-16 LE INI in the host's active plugin configuration directory, select the actual adapter, and restart the host.
4. 在运行目录 `Telemetry/cpu-source.txt` 写入 `trafficmonitor`，重启 Touch Bar。若此前安装独立采集任务，先用 `tools/GameTelemetry/Configure-Autostart.ps1 -Mode Disable` 关闭该任务。 / Write `trafficmonitor` to the runtime marker and restart Touch Bar; disable any previously installed independent collector task.

| 字段 / Field | 口径 / Meaning |
|---|---|
| CPU 温度 / temperature | 原生 Core Average，多核心温度平均；不是最近几秒的平均，也不使用 Package/Core Max / Native Core Average, never a moving time average or Package/Core Max |
| CPU 占用 / load | TrafficMonitor 主程序 CPU 值 / Host CPU utilization |
| CPU 频率 / clock | 主程序 CPU_FREQ，GHz 转 MHz；不是各核频率算术平均 / Host CPU_FREQ converted from GHz to MHz, not a mean of core clocks |
| GPU 温度 / temperature | 所选 GPU Core 传感器；不混合 Hot Spot 等不同测点 / Selected GPU Core sensor, without mixing unrelated sensor locations |
| GPU 占用 / load | GPU Core，缺少时 D3D 3D；保持原独立采集口径 / GPU Core, falling back to D3D 3D |
| GPU 频率 / clock | 所选 GPU Core 的 MHz 缓存文本 / Selected GPU Core clock cached in MHz |

桥接包超过 3 秒未更新会被拒绝。一个包代表同次桥接回调，**不代表所有传感器在物理上同时采样**。HardwareMonitor 没有提供独立传感器时间戳，内部缓存停滞无法仅凭桥接时间戳判断；升级宿主或插件后需重新验证兼容性。

Packets older than three seconds are rejected. One callback packet does not guarantee simultaneous physical sampling. HardwareMonitor exposes no independent sensor timestamp, so a stalled internal cache cannot be identified solely from bridge freshness. Retest compatibility after host/plugin upgrades.

## FPS 与可选独立采集 / FPS and optional independent collection

从 [依赖来源清单](../WindowsTouchBar-master/WindowsTouchBar/Telemetry/provenance.json) 指向的官方发布获取 PresentMon 2.6.0，核对 SHA-256，改名 `PresentMon.exe` 放入源码或运行目录的 `Telemetry/`。FPS 取目标游戏 PID 中最活跃 swapchain 的应用呈现间隔，不累加多个交换链，不向游戏注入 DLL；它不等于显示器实际显示帧数。ETW 权限或游戏兼容性不足时可显示缺失值。

Obtain the official PresentMon 2.6.0 binary from the provenance manifest, verify its hash and place it as `Telemetry/PresentMon.exe`. FPS uses the target PID's busiest swapchain and application presentation intervals; it neither sums swapchains nor injects into the game, and is not the monitor's displayed-frame count. ETW permission or game compatibility can limit availability.

不用 TrafficMonitor 时可选择 [独立采集器](../tools/GameTelemetry/README.md)。它需要另行准备 LibreHardwareMonitor；CPU 温度通常需要管理员硬件访问。全新克隆无需这些可选二进制也可以构建和执行离线检查。编译不会安装采集任务、启动硬件读取或变更驱动。

An optional independent collector is available when not using TrafficMonitor. It needs LibreHardwareMonitor and typically elevated hardware access for CPU temperature. Fresh clones build and run offline checks without those optional binaries; building does not install tasks, start hardware collection or change drivers.
