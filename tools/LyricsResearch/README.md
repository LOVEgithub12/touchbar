# GitHub Touch Bar 歌词项目研究

查询时间：2026-11-08。任务目标为本机 Windows 10 / T2 Touch Bar，播放器为用户指定的 `C:\Program Files\foobar2000`。

## 选定参考

**[reihnxx/lyricbar](https://github.com/reihnxx/lyricbar)**

- MIT 许可证；Copyright (c) 2026 Reihan Hafidz and LyricBar contributors。复刻或移植 substantial portions 时保留 LICENSE 与版权声明。
- 固定提交：`4c57407bd8149d16b0bd82c44fac7c8d2d408826`，最后推送 2026-11-03。
- **实际平台限制**：歌词来源是 Spotify Web Player 浏览器扩展；Windows/Linux 只提供悬浮窗，Touch Bar 支持来自 macOS Swift AppKit/DFRFoundation。不是 Windows Touch Bar 驱动，不直接支持 foobar2000。
- 已保存 README、LICENSE、架构、协议、LRC 解析、LRCLIB provider、匹配、同步与 Touch Bar 渲染源码，见 `lyricbar/`。未运行上游安装脚本、扩展或二进制。

## 可复刻部分

| 文件 | 可复用行为 |
|---|---|
| extension/lib/lrc.js | `[mm:ss.xx]` / `[mm:ss.xxx]`、每句多个时间标签、`[offset:]`、排序、空句作为间奏 |
| extension/lib/sync.js | 二分定位当前句；按播放/暂停与基准时间插值；下一句时间点切换；跳转时重算 |
| extension/lib/providers/lrclib.js | LRCLIB `/api/get` 精确签名查找，随后 `/api/search`，无 API key；请求头 `Lrclib-Client` |
| extension/lib/match.js | 标题/艺人相似度与时长匹配，避免错歌歌词；默认时长差 4 秒，近似候选 15 秒 |
| apps/macos/Sources/LyricBar/LyricView.swift | 当前句白色、下一句较暗；可容纳则居中；长句先缩小再滚动；单句变化淡入 |

本机应继续使用已工作的 C# USB/IP 显示与触摸层，将显示/同步行为移植到新的歌词视图。foobar2000 的真实曲目与播放进度应从其本地可用接口取得，优先同名/内嵌 LRC，找不到时再按元数据查询 LRCLIB。需要网络查询时只发送歌名、艺人、专辑、时长；不发送文件路径、音乐文件或凭据。

上游 `spreadPlainLyrics` 将无时间戳文本均分到整首歌，并标记 `synced: false`。这不是精确同步；本机不要把这种均分时序当真实歌词时间。可明确展示静态歌词/未同步状态或回退歌名。

## 其他候选

| 仓库 | 维护/许可 | 适配结论 |
|---|---|---|
| [RidhaAF/lirik](https://github.com/RidhaAF/lirik) | Swift，2026-08-25，GitHub 未识别许可证 | macOS，不采用无明示许可源码 |
| [ShiyangZheng/touchbar-lyrics](https://github.com/ShiyangZheng/touchbar-lyrics) | Swift，2026-04-30，GitHub 未识别许可证 | macOS Spotify，不直接适合本机 |
| [Sep26JI/touchbar-lyrics](https://github.com/Sep26JI/touchbar-lyrics) | Swift，2026-10-02，GitHub 未识别许可证 | macOS QQ 音乐，不直接适合本机 |
| [therealclvn/TouchBarLyrics](https://github.com/therealclvn/TouchBarLyrics) | MIT，2026-10-31 | macOS Spotify/Apple Music，可作为备选视觉参考 |
| [regorxxx/foo_httpcontrol_fb2kc](https://github.com/regorxxx/foo_httpcontrol_fb2kc) | BSD-3-Clause，2025-05-20 | foobar2000 本地 HTTP 控制接口，若本机已有组件可用于曲目/进度 |

GitHub repository search `touchbar lyrics foobar2000` 返回 0 项；`touchbar lyrics windows` 的两项也未提供直接在 Windows T2 Touch Bar 上驱动歌词的现成实现。这个结论仅限本次仓库搜索范围，不能证明整个 GitHub 不存在其他实现。
