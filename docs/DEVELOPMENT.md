# 开发说明 / Development

[中文首页](../README.md) · [English home](../README.en.md)

## 架构 / Architecture

| 模块 / Module | 职责 / Responsibility |
|---|---|
| `TouchBar.cs` | 视图选择、触摸和生命周期，设备 ACK 后重臂动画计时器 / View selection, touch and lifecycle; animation timer rearmed after device ACK |
| `Device/UsbIpTouchBarTransport.cs` | 连接本机 usbipd 的显示和触摸传输 / Display and touch through local usbipd |
| `Views/`、`Components/` | 生产 Skia 视图、布局、触摸状态和按钮 / Production Skia views, layout, touch state and buttons |
| `Lyrics/FoobarLyricsService.cs` | 读取桥接文件、处理新鲜度与本地歌词 / Bridge state, freshness and local lyric resolution |
| `LyricsPlaybackClock`、`LyricsFrameAnimator`、`LyricsViewport` | 单调插值、每帧时间轴、长歌词滚动 / Monotonic interpolation, per-frame timeline and long-line scrolling |
| `Audio/AudioEnergyService.cs` | WASAPI 共享回环、端点重连和派生能量 / Shared loopback, endpoint reconnection and derived energy |
| `AudioSpectrumAnalyzer` | 双 FFT 窗与对数频带、声道功率合并 / Dual FFT windows, logarithmic bands and combined channel power |
| `AuroraRibbonRenderer` | 绸带插帧、能量包络、星空和两侧星尘 / Ribbon interpolation, energy envelope, stars and side dust |
| `SpectrumBackgroundRenderer` | 频谱填充、历史、独立粒子发射与消退 / Spectrum fill, history and independent particle emission/decay |
| `Interop/NativeVolumeControl.cs` | 独立音量命令发送到 Shell / Standalone volume commands sent to the Shell |
| `TouchBar-USBIP/` | 手动与登录启动、UTF-8 状态解析、停止与取消 / Manual/login launch, UTF-8 state parsing, stop and cancellation |

数据流 / Data flow:

```text
foobar2000 panel -> foobar-state.json -> lyric service -> playback clock -> LyricsView
WASAPI loopback  -> band / spectrum analyzers          -> background renderers
foreground app + explicit page state                  -> view selection
Skia frame -> local USB/IP -> Touch Bar display
Touch Bar touch -> USB/IP -> active view -> command / navigation / background selection
```

音频反映输出混音，不进行人声分离。歌词时间来自播放器，不用波形猜测歌词。背景偏好、运行日志与播放器状态均为本地数据。

Audio reflects the output mix, without vocal separation. Lyric timing comes from the player rather than waveform inference. Background preference, runtime logs and playback state remain local.

## 构建 / Build

从仓库根目录、Windows x64 环境执行 / From the root on Windows x64:

```powershell
pwsh -NoProfile -File .\tools\Build.ps1
# Optional: prepare the launcher directory after stopping an existing app
pwsh -NoProfile -File .\tools\Build.ps1 -PrepareRuntime
```

支持用 `-DotNetPath` 指定 SDK 可执行文件。默认先恢复 NuGet，再构建 Release；不要在全新克隆上跳过 restore。构建助手不启动应用，不改驱动；准备运行目录前需停止已运行的同路径程序。

Use `-DotNetPath` for a custom SDK executable. Restore precedes the Release build; do not skip restore on a fresh clone. The helper does not launch the app or change drivers. Stop a running instance before preparing its runtime directory.

## 自动检查 / Automated checks

```powershell
pwsh -NoProfile -File .\tools\Check.ps1
# Explicit runtimes are also supported:
pwsh -NoProfile -File .\tools\Check.ps1 -DotNetPath 'C:\path\dotnet.exe' -NodePath 'C:\path\node.exe'
```

| 检查 / Check | 覆盖 / Coverage |
|---|---|
| RuntimeChecks | 实际视图与触控源码，23 项：命令释放、音量分流、显式 F 页、背景循环、频谱分离/静音/反相、粒子滞留、平滑包络、边缘/布局 / Actual view/touch source; 23 checks for command release, volume routing, explicit F page, backgrounds, spectrum/silence/stereo phase, particles, envelopes, edges and layout |
| LyricsChecks | LRC、本地匹配、服务状态、偏好 / LRC, local matching, service state and preferences |
| NavigationChecks | 触摸释放后的 4 秒返回、按住保护 / Four-second return after release and held-touch protection |
| PlaybackClockChecks | 插值、跳转、漂移和陈旧样本 / Interpolation, seeks, drift and stale samples |
| LyricsViewportChecks | 居中、演唱前沿跟随与滚动平滑 / Centering, singing-front tracking and easing |
| FontChecks | 当前系统中英日韩字体与混排字形，依赖安装字体 / Installed CJK/Latin font coverage and mixed scripts; requires those fonts |
| FnKeyChecks | 保留的历史 Fn 推断 helper / Retained legacy Fn inference helper |
| UsbIpHostChecks | 假本机 USB/IP 服务，序号分发、取消竞态、帧与触摸协议 / Fake local host, request routing, cancellation races, frame and touch protocol |
| StartupChecks | UTF-8、stderr、错误/缺失 JSON、超时进程 / UTF-8, stderr, malformed/missing JSON and process timeout |
| AutostartLifecycleChecks | PS5.1 隔离副本、启动取消、失败检测 / Isolated PS5.1 copies, cancellation and failure detection |
| Test-FoobarBridge.cjs | 假 JSplitter、状态、命令去重/验证和重载 / Mock JSplitter state, command deduplication/validation and reload |

检查不连接真实 USB，不发送真实键盘输入，不更改驱动或登录项；USB/IP 协议检查使用本机假服务。启动检查会创建并清理临时测试进程。FontChecks 需要 Windows CJK 字体，精简系统可能需要补齐对应语言字体。RuntimeChecks/FontChecks 引用主程序 Release 输出里的依赖，所以必须先构建主程序；`Check.ps1` 已按此顺序执行。

Checks do not open real USB devices, send real keyboard input or change drivers/login entries. USB/IP checks use a fake local service. Startup checks create and clean up temporary fixture processes. FontChecks needs Windows CJK fonts, which minimal systems may lack. RuntimeChecks/FontChecks reference the application's Release dependencies, so build the app first; `Check.ps1` does this automatically.

GitHub Actions 通过 `.github/workflows/checks.yml` 在 Windows 上运行相同入口。自动化成功只表示离线行为通过，不能证明实际设备、驱动或系统重启正常。

GitHub Actions runs the same entry point on Windows through `.github/workflows/checks.yml`. A passing job establishes offline behavior, not hardware, driver or reboot success.

## 效果图复现 / Reproducing the previews

先构建应用和 RuntimeChecks，再运行： / Build the app and RuntimeChecks first, then run:

```powershell
dotnet run --project .\tests\RuntimeChecks\RuntimeChecks.csproj -c Release -- --render-docs .\preview-output
python -m pip install Pillow
python .\tools\ComposeDocumentation.py .\preview-output .\docs\images
```

`--render-docs` 使用生产 `MediaFunctionsView`、`DefaultFunctionsView`、`LyricsView`、真实频谱分析器与两个背景渲染器。合成谐波先进入分析器；没有读取个人音乐、播放器或参考视频。约 7 秒生成 6 秒的两套动画帧，合成器输出 20 fps 循环 GIF、静态页面和总览；细节 GIF 是中区右侧的 2 倍局部放大。只需 Pillow，不需要视频编码器。

`--render-docs` uses the production media/function/lyric views, actual spectrum analyzer and both background renderers. Synthetic harmonics pass through the analyzer; it reads no personal music, player or reference video. Roughly seven seconds produce six-second animations, then the composer emits 20 fps looping GIFs, static panels and an overview. The detail GIF enlarges a right-center crop by 2×. Only Pillow is required, without a video encoder.

字体与实际执行时钟会产生少量像素差异。`preview-output` 是临时产物，不应整包提交；公开文档图片只放 `docs/images`。绸带资源图集本身属于生产资源，见授权说明。

Fonts and wall-clock timing can produce small pixel differences. Do not commit the entire temporary `preview-output`; intentional public images belong in `docs/images`. The ribbon atlas is a production asset; see the notices for provenance.

## 实机验收 / Hardware acceptance

| 项目 / Item | 验证 / Verification |
|---|---|
| 传输 / Transport | 首帧 ACK、正确尺寸、连续显示与触摸 / First-frame ACK, correct size, continuous display and touch |
| 控制 / Controls | 单击、长按、滑出、静音、F 页往返；管理员任务管理器前台音量 / Tap, hold, slide-out, mute, F-page round trip; volume over elevated Task Manager |
| 歌词 / Lyrics | 换歌、seek、暂停、长句、中英日韩；4 秒回页不打断长按 / Track changes, seeks, pause, long/mixed text; idle return does not interrupt a hold |
| 背景 / Backgrounds | 循环与重启保留、安静/高低频、粒子消退、边缘不截断 / Cycling/persistence, quiet/bass/treble response, particle decay and blended edges |
| 页面 / Pages | 播放时浏览器不抢栏，暂停后能换栏 / Browser cannot replace playback controls, but can take priority after pause |
| 启动 / Startup | 正常登录、设备延迟、停止取消、完整重启、睡眠唤醒 / Normal login, delayed device, cancellation, full reboot and suspend/resume |

本次发布的完整重启与睡眠唤醒不作为已经完成的验收项。请在变更说明里区分新增自动检查与真正完成的设备验证。

Full reboot and suspend/resume are not claimed as completed acceptance for this publication. Distinguish new automated checks from actual device validation in change reports.
