# 安装与更新

[English](INSTALL.en.md) · [首页](../README.md) · [功能与排查](USAGE.md)

## 1. 支持范围与准备

| 项目 | 要求 / 验证情况 |
|---|---|
| 已测设备 | MacBookPro16,1，Apple T2，Touch Bar 2008 × 60 |
| 已测系统 | Windows 10 Pro 22H2，build 19045；Windows 11 未完成实机验收 |
| USB 设备 | 当前脚本限定 `USB\VID_05AC&PID_8302\0000000000000000`，不会自动绑定任意 USB 设备 |
| Boot Camp | 已有正常的 Apple 相关设备支持；亮度/背光依赖对应接口 |
| 构建 | [.NET 6 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/6.0)，本次验证 6.0.428；只有运行时不能编译 |
| 脚本 | [PowerShell 7](https://github.com/PowerShell/PowerShell/releases) 的 `pwsh.exe` 在 PATH 中；Windows PowerShell 5.1 用于登录/恢复兼容路径 |
| 传输 | [usbipd-win 5.3.0](https://github.com/dorssel/usbipd-win/releases/tag/v5.3.0)，x64 官方安装包；未验证任意新版的命令契约 |
| 歌词，可选 | foobar2000 的 foobox/JSplitter 环境；当前自动安装器针对下述固定 profile 布局 |
| 测试，可选 | Node.js 22，用于桥接脚本的离线检查 |

这是现有硬件环境上的源码项目，尚不是面向所有 Boot Camp 机型的一键安装器。当前路线无需 WSL、关闭 Secure Boot 或启用测试签名。仓库中的旧驱动参考不用于这套流程。

## 2. 构建源码

在 PowerShell 7 中执行，工作目录为仓库根目录：

```powershell
git clone https://github.com/LOVEgithub12/touchbar.git
cd touchbar
.\tools\Build.ps1 -PrepareRuntime
```

若使用用户目录 SDK：

```powershell
.\tools\Build.ps1 -DotNetPath "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" -PrepareRuntime
```

`Build.ps1` 自动恢复 NuGet 依赖，将应用构建到 `WindowsTouchBar-master\WindowsTouchBar\bin\Release\net6.0-windows`。`-PrepareRuntime` 再把它复制到启动器使用的 `TouchBar-USBIP\app`；已有 `settings.json` 会保留。它拒绝覆盖仍在运行的同路径程序。不要从仓库中寻找已提交的 EXE，它不属于本次源码发布范围。

首次运行需要 .NET 6 Windows Desktop x64 运行时；在本机用 Windows .NET SDK 构建时通常已一并具备。`dotnet --list-sdks` 和 `dotnet --list-runtimes` 可用于确认实际安装。

## 3. 配置 USB/IP（管理员操作）

### 3.1 安装官方传输组件

从上面的 usbipd-win 官方 5.3.0 发布页下载 **`usbipd-win_5.3.0_x64.msi`**，放到仓库的 `tools\downloads\`。安装包不会由本仓库重新分发。

在**管理员 PowerShell 7**、仓库根目录执行：

```powershell
.\tools\InstallTouchBarUsbIp.ps1
```

脚本检查 Authenticode 发布者与固定 SHA-256：

```text
1C984914AEC944DE19B64EFF232421439629699F8138E3DDC29301175BC6D938
```

它安装 Server/Drivers，并禁用安装器的 `usbipd` 局域网入站防火墙规则，因为本项目客户端只连接 `127.0.0.1:3240`。若已安装 usbipd，脚本会停止而不是覆盖现有安装；先检查现有版本与服务。不要为本机使用目的开放公网端口。

### 3.2 绑定 Touch Bar

仍在管理员 PowerShell 7 执行：

```powershell
.\tools\BindTouchBarUsbIp.ps1 -Mode Bind
```

该脚本从 usbipd 状态获取当前 bus ID，并检查确切设备身份。无需手写 `1-6` 等可能变化的总线编号。绑定会把 Touch Bar 显示设备交给 VBoxUSB；停止本程序后，不会自动恢复 Apple 原显示服务。

### 3.3 普通用户启动

关闭管理员终端，在普通桌面用户会话中双击根目录 `Start-Media.cmd`，或执行：

```powershell
.\Start-Media.cmd
```

启动器验证设备和运行状态；初次绑定不足时可能单独请求管理员权限。主应用必须回到普通桌面会话运行。不要把“以管理员身份运行整个应用”当作通用修复方式。

诊断时先 `Stop.cmd`，再使用 `TouchBar-USBIP\Check.cmd`。检查日志位于 `TouchBar-USBIP\app\WindowsTouchBar.status.log`；成功运行应有 `RUNNING: ... first frame acknowledged`。`Check` 会短暂访问设备，它不是离线单元测试。

## 4. 配置 foobar2000 歌词（可选）

媒体/F 键页不需要歌词桥接。自动桥接安装器针对已有的：

```text
C:\Program Files\foobar2000\profile\foobox\script\js_panels\bottombar.js
C:\Program Files\foobar2000\profile\foobox\script\js_common\
```

先备份并关闭或卸载播放控制面板，在管理员 PowerShell 中执行：

```powershell
.\tools\InstallFoobarTouchBarBridge.ps1
```

脚本备份原面板，安装 `touchbar_bridge.js`，只追加一次 include，并把**当前仓库的 app 绝对路径**写入安装副本。重载 JSplitter 面板或重启 foobar2000 后，播放带时间戳歌词的曲目。个人曲目状态只保存在本地 `app\foobar-state.json`，不应提交到 Git。

其他 profile 位置不能直接套用固定路径安装器。请按[桥接协议](../tools/LyricsResearch/FOOBAR-BRIDGE.md)手动配置 `TouchBarBridgeOutputPath`、复制桥接并追加 include。更换仓库位置后需重新配置桥接路径。

支持本地同名 `.lrc`、profile 歌词目录及内嵌时间戳歌词；不联网抓取。详见[歌词使用](USAGE.md#歌词与背景)。

## 5. 登录启动与故障恢复（可选）

普通用户运行根目录 `Enable-Autostart.cmd` 写入 **HKCU Run**；`Disable-Autostart.cmd` 关闭。它是**用户登录后**启动，不是登录前显示服务。启动器等待 usbipd/设备就绪，有限次重试，停止命令可取消等待中的启动。

针对已确认的 VBoxUSB 开机 Code 37 / `C0000034`，另有管理员安装器：

```powershell
.\tools\InstallTouchBarBootRecovery.ps1
```

它将恢复脚本复制到受保护的 `%ProgramFiles%\TouchBar-Startup` 并建立 SYSTEM 开机任务，而不让高权限任务执行用户可写的仓库脚本。只用于对应故障，不能代替安装检查。已有正常恢复任务时无需重复安装。

最新 UTF-8 设备状态解析及启动取消逻辑已测试；修复后最终整机重启仍需再次验收。迁移目录后，应重新设置登录启动与桥接路径。

## 6. 更新与回退

更新已有安装：

```powershell
.\Stop.cmd
git pull --ff-only
pwsh -NoProfile -File .\tools\Build.ps1 -PrepareRuntime
.\Start-Media.cmd
```

更新前保留自己的 `settings.json` 与 `visualizer-preference.json`。有本地源码改动时，先正常提交或备份，解决合并再构建；不要用强制重置丢弃个人配置。

要恢复 Apple 原显示驱动：先停止本程序，再运行 `TouchBar-USBIP\Restore-Apple.cmd` 并接受驱动操作的管理员提示。它与“停止程序”是不同动作。恢复工具检查设备状态；可先查看[排查说明](USAGE.md#故障排查)。

卸载歌词桥接时，只移除自己追加的 include，再重载面板；不要用整份旧备份覆盖后来做过其他修改的播放器面板。关闭自启动后，可保留或删除自己的运行目录。usbipd 若供其他用途使用，不应随本程序一起卸载。
