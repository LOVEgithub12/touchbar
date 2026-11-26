# TrafficMonitor CPU/GPU bridge

This optional x64 plugin publishes CPU and GPU readings already collected by TrafficMonitor to the ordinary Touch Bar process. It starts no thread or process, installs no driver, and performs no hardware polling. It has no visible item and does not change TrafficMonitor's taskbar layout.

## Metrics

| Field | Source |
|---|---|
| CPU temperature | HardwareMonitor's native `Core Average`, with host fallback only if its temperature selector is exactly `Core Average`; never Package/Core Max or a moving time average |
| CPU load | `ITrafficMonitor.GetMonitorValue(MI_CPU)` |
| CPU clock | Host `MI_CPU_FREQ` GHz multiplied by 1000 to MHz; Windows performance-counter metric, not an arithmetic mean of core clocks |
| GPU temperature | Configured adapter's `Temperature/GPU Core`; not Hot Spot or an average across unrelated sensors |
| GPU load | Configured adapter's `Load/GPU Core`, falling back to `Load/D3D 3D` if Core is unavailable; matches the former Touch Bar collector's preference |
| GPU clock | Configured adapter's `Clock/GPU Core`, in MHz |

Temperature/load use the HardwareMonitor plugin's public numeric graph cache, checked against its missing-value text. Clock uses its public cached value text because its numeric graph API does not expose clock sensors. Clock requires explicit **MHz** (decimal dot/comma supported). The inspected HardwareMonitor version converts GHz using 1024, so GHz text is deliberately rejected. Valid zero load/clock is preserved.

GPU selection is explicit: create `TouchBarCpuBridge.ini` in TrafficMonitor's **active plugin configuration directory**, using UTF-16 LE for the Win32 INI reader:

```ini
[bridge]
gpu_identifier=/gpu-amd/0
```

This identifier is machine-specific. Configure that adapter's `Temperature/GPU Core`, `Load/GPU Core` (or `Load/D3D 3D`), and `Clock/GPU Core` items in HardwareMonitor. Keep `Core Average` configured for CPU. These items may be hidden from the taskbar. For the clock item select MHz and show its unit. Restart TrafficMonitor after changing the adapter or configured item list.

## Build, install and runtime

`Build.ps1 -ZigPath <zig.exe>` builds `build/TouchBarCpuBridge.dll` using Microsoft's x64 C++ ABI and Win32 import libraries; version 2.0.1 was validated with Zig 0.16.0. `Test-ABI.ps1 -ZigPath <zig.exe>` uses isolated fixture mappings, checks v1/v2 compatibility, units, missing/idle values, 10,001 updates with metadata calls forbidden, unload invalidation, and v2 restart with a retained reader. Repository RuntimeChecks cover atomic reads, numeric/source validation and freshness.

Copy the DLL to TrafficMonitor's existing `plugins` directory while it is closed, then start TrafficMonitor with its existing privilege level and configuration. Close it normally to preserve settings and traffic history. Do not restart/reconfigure it on every Touch Bar launch.

HardwareMonitor must be loaded before this bridge. The verified host enumerates `HardwareMonitor.dll` before `TouchBarCpuBridge.dll` and calls their acquisition callbacks sequentially on one thread. Sensor pointers are bound once in `OnInitialize`, before acquisition starts. **Do not move `GetItemId()` back into `DataRequired()`:** HardwareMonitor implements it with a shared mutable `std::wstring`, also called by the UI thread. Cross-thread enumeration can corrupt the host heap. The bridge never calls HardwareMonitor's `DataRequired()`, never loads another hardware DLL, and never scans private memory. If the hardware plugin/items are unavailable at initialization, missing fields remain unavailable until restart.

Write `trafficmonitor` to the application's `Telemetry/cpu-source.txt` and restart Touch Bar with its correct transport arguments. `GameTelemetryService` then reads all six CPU/GPU fields from one packet, skips the elevated pipe and **does not launch TouchBarSensors**. PresentMon remains separate because FPS/frame time are game presentation metrics. The existing silent ordinary-user login task is unchanged. Any old automatic hardware-collector task should remain disabled in bridge mode; retain its installation for rollback.

## Protocol and limitations

`Local\TouchBar.TrafficMonitor.Telemetry.v2` is a 128-byte, session-local mapping. The producer publishes a versioned record under an interlocked odd/even sequence counter: QPC timestamp/frequency, PID, six validity bits and values, source markers, and callback timing counters. Touch Bar opens it read-only. There are no commands or network endpoints. A snapshot contains CPU/GPU values from one bridge callback; this does not mean hardware sensors were physically sampled at exactly the same instant.

The reader rejects torn, future, incompatible, and older-than-three-second packets. Invalid fields display `—`; there is no implicit second collector or all-fields waiting barrier. Retained readers recover after the producer restarts. The legacy 96-byte `Local\TouchBar.TrafficMonitor.Cpu.v1` mapping remains published for rollback to the prior CPU-only application; the current service uses v2.

HardwareMonitor exposes no independent sample timestamp or acquisition-failure status. Freshness is therefore callback freshness; internal failures that leave its old cache cannot be independently detected. Future host/plugin versions require compatibility testing. A short live test does not establish unlimited uptime.

To return to independent CPU/GPU collection, remove the runtime `Telemetry/cpu-source.txt` marker, configure the optional [sensor collector](../GameTelemetry/README.md), and restart Touch Bar. Keep that collector disabled while measuring bridge resource use.

## Upstream provenance

- `upstream/PluginInterface.h` is unmodified from TrafficMonitor V1.86, commit `02a817a069bac6bf4d263b5209d9c1b07fe2f950`, blob `f11f283302938d26bab668a3afdd07b1917814e3`. Preserve `upstream/LICENSE` (Anti 996 License).
- Host units, startup and callbacks were checked in `TrafficMonitor.cpp`, `TrafficMonitorDlg.cpp`, `PluginManager.cpp`, and `PdhHardwareQuery/CpuFreq.cpp` at that commit.
- Public item behavior and lifetimes were checked in official `zhongyang219/TrafficMonitorPlugins`, commit `eb7fc9e56f93fb69c99b185c6b8c395153d78bc6`, `Plugins/HardwareMonitor/HardwareMonitor.cpp`, `HardwareMonitorItem.cpp`, and `HardwareMonitorHelper.cpp`.
- Game configuration, installation and metric definitions: [game monitoring guide](../../docs/GAMING.md).
