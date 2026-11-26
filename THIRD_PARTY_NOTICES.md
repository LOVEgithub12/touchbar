# 第三方、资源与授权 / Third-party, asset and licensing notices

本仓库包含上游源码、本地修改、独立工具、视觉资源和研究参考。目前没有一份已确认能覆盖全部内容的统一许可证；**公开可见不等于所有内容已获得同一种再分发授权**。本文件记录已知范围，不重新授权第三方作品。

The repository includes upstream source, local modifications, independent tools, visual assets and research references. No single verified license covers all of it. **Public visibility does not establish one redistribution license for every file.** This document records known scope and does not relicense third-party work.

| 组件 / Component | 来源与范围 / Origin and scope | 许可证 / License |
|---|---|---|
| WindowsTouchBar application base | `WindowsTouchBar-master/`，基于已有 WindowsTouchBar 工程，历史 README 指向 Naozumi520 的相关驱动；当前 C# 应用在其上持续修改 / Existing WindowsTouchBar project, subsequently modified; its historical README referenced Naozumi520's related driver | 本地导入目录未发现 LICENSE，不能据此声明整应用 MIT / No LICENSE found in the imported application directory; MIT coverage for the whole app is not established |
| DFRDisplayKm reference | [BingxingWang/DFRDisplayKm](https://github.com/BingxingWang/DFRDisplayKm)，`DFRDisplayKm-master/`，历史参考 / Historical reference | [MIT](DFRDisplayKm-master/LICENSE)，保留原版权 / Original copyright retained |
| lyricbar research snapshot | [reihnxx/lyricbar](https://github.com/reihnxx/lyricbar)，固定提交 `4c57407bd8149d16b0bd82c44fac7c8d2d408826`，`tools/LyricsResearch/lyricbar/` | [MIT](tools/LyricsResearch/lyricbar/LICENSE)，2026 Reihan Hafidz and LyricBar contributors |
| NAudio | [naudio/NAudio](https://github.com/naudio/NAudio)，NuGet `NAudio.Wasapi 2.2.1` | [MIT notice](WindowsTouchBar-master/WindowsTouchBar/Audio/NAudio-LICENSE.txt)，随构建复制 / Copied with build output |
| USB/IP native probe | `tools/UsbIpNativeProbe/` | [该目录许可证 / Local license](tools/UsbIpNativeProbe/LICENSE) |
| WSL bridge reference | `tools/TouchBarWslBridge/`，非当前运行路线 / Not the current transport | [该目录许可证 / Local license](tools/TouchBarWslBridge/LICENSE) |
| NuGet dependencies | HidSharp 2.1.0, InputSimulatorCore 1.0.5, SkiaSharp 2.88.0, SkiaSharp.Elements 1.0.3, System.Management 6.0.0 | 各包独立许可证见 NuGet 包元数据及上游，不由本文统一覆盖 / Each package retains its NuGet/upstream license |
| usbipd-win / VBoxUSB | [dorssel/usbipd-win](https://github.com/dorssel/usbipd-win)，外部安装的服务和签名驱动 / Externally installed service and signed driver | 遵循官方分发条款；本仓库不包含安装器或驱动二进制 / Official distribution terms; no installer or driver binaries included here |

## 视觉与文档资源 / Visual and documentation assets

- `Resources/aurora-ribbon-frames.png` 是由用户提供的参考视频加工得到的生产绸带图集，原视频不在本次发布内容中。未提供该素材的单独通用授权，不能把它视为 CC0 或自动纳入第三方源码许可证。 / `Resources/aurora-ribbon-frames.png` is a production ribbon atlas derived from a user-provided reference video. That video is excluded from this update. A separate general asset license has not been supplied; it should not be treated as CC0 or automatically covered by a source dependency's license.
- `Resources/spectrum-atlas-v3.json` 与 `spectrum-response.json` 是根据用户参考提取或校准的轮廓/响应数据；保留其来源边界，不自动声明为通用开源视觉素材。 / The spectrum atlas and response resources are contour/calibration data derived from user references, without a separate general-purpose visual-asset license.
- `Resources/foobar2000.png` 是播放器识别图标；foobar2000 的名称、标志与图标属于各自权利人，不表示官方合作。 / The foobar2000 resource identifies the player; its name, marks and icon remain with their rights holders and imply no affiliation.
- `Resources/chrome.png` 从本机安装的 Chrome 应用图标提取，用于浏览器页面识别；Chrome 名称和标志属于 Google，不表示官方合作或背书。 / `Resources/chrome.png` was extracted from the locally installed Chrome application icon to identify its browser page. The Chrome name and logo belong to Google and imply no affiliation or endorsement.
- `docs/images/` 为应用离屏渲染。`midnight-city-demo.gif` 含 M83《Midnight City》的歌词片段与音乐分析驱动的视觉，无音频；歌曲与歌词权利属于各自权利人，本文不重新授权。其余演示使用合成谐波和示例文字。原歌曲、歌词源文件及参考视频不包含在仓库中。 / Documentation images are offscreen renders. The Midnight City GIF contains a lyric excerpt and visuals driven by music analysis, without audio; song and lyric rights remain with their holders. Other demos use synthetic harmonics and sample text. Original songs, lyric files and reference videos are excluded.
- Apple、Microsoft、Windows 等名称和商标属于其各自权利人。 / Apple, Microsoft, Windows and other names/marks belong to their respective holders.

保留各目录现有 LICENSE、版权头和引用。若计划将本仓库作为产品再分发，应先确认应用基底及视觉素材的完整授权范围；不要用新增一个根目录 MIT 文件掩盖来源差异。

Retain existing LICENSE files, copyright headers and references. Before redistributing this repository as a product, establish the complete permissions for the application base and visual assets. Adding a root MIT file would not resolve those differing origins.

## 可选游戏监控依赖 / Optional game monitoring dependencies

- LibreHardwareMonitorLib：本机 TrafficMonitor 安装中的 0.9.4，程序集标注提交 `b8077435b898d57539956388cddf49f7dacb86f7`。上游源码 https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/tree/b8077435b898d57539956388cddf49f7dacb86f7 ，MPL-2.0，许可证随 `Telemetry/LibreHardwareMonitor-LICENSE.txt` 保留；DLL 未修改。
- PresentMon：GameTechDev 官方签名控制台版本 2.6.0，MIT 许可证随 `Telemetry/PresentMon-LICENSE.txt` 保留；下载来源与 SHA-256 见 `Telemetry/provenance.json`。二进制不纳入 Git。
- `Resources/Games/`：用户指定的本机游戏图标，仅作游戏识别；权利属于原作者，不重新授权。带本机游戏绝对路径的 `game-monitor.json` 不纳入 Git。
- `docs/game-monitor-preview/index.html` 内嵌同一组游戏识别图标与原音量图标，用于用户确认的交互布局预览；固定数值不是实时监控。图标权利仍归各自权利人。 / The interactive preview embeds the same game-identification and volume icons, with fixed example readings rather than live telemetry. Icon rights remain with their holders.


## TrafficMonitor plugin interface

The optional CPU/GPU bridge includes the unmodified V1.86 interface from zhongyang219/TrafficMonitor (commit 02a817a069bac6bf4d263b5209d9c1b07fe2f950). Its Anti 996 License is preserved in [tools/TrafficMonitorBridge/upstream/LICENSE](tools/TrafficMonitorBridge/upstream/LICENSE). See the bridge README for API provenance and runtime dependencies.
