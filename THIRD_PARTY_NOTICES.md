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
- `Resources/foobar2000.png` 是播放器识别图标；foobar2000 的名称、标志与图标属于各自权利人，不表示官方合作。 / The foobar2000 resource identifies the player; its name, marks and icon remain with their rights holders and imply no affiliation.
- `docs/images/` 是当前应用代码的离屏绘制效果图，输入为本仓库生成器中的合成谐波与演示文字，不包含歌曲音频、私人歌词文件或原始视频帧。截图中仍会显示上述应用资源。 / `docs/images/` contains offscreen application renders driven by synthetic harmonics and demonstration text from the generator. No song audio, private lyric files or raw reference-video frames are included. The renders still display the application assets noted above.
- Apple、Microsoft、Windows 等名称和商标属于其各自权利人。 / Apple, Microsoft, Windows and other names/marks belong to their respective holders.

保留各目录现有 LICENSE、版权头和引用。若计划将本仓库作为产品再分发，应先确认应用基底及视觉素材的完整授权范围；不要用新增一个根目录 MIT 文件掩盖来源差异。

Retain existing LICENSE files, copyright headers and references. Before redistributing this repository as a product, establish the complete permissions for the application base and visual assets. Adding a root MIT file would not resolve those differing origins.
