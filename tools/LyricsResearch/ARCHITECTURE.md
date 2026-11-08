# Touch Bar 歌词功能接入设计（本地架构审查）

## 当前程序边界

`TouchBar` 创建传输、`LockedFrameBuffer`、Skia canvas 和四类视图；`Init()` 在非 UI 工作线程中阻塞等待停止事件。所有视图通过 `OnInvalidate` 回调串行进入 `viewLock`，然后 `Clear`、`Draw` 和 `FrameBuffer.VSync()`。程序没有 WinForms/WPF 主消息循环。

`ForegroundTracker` 只有前台窗口轮询（200 ms），不能提供歌曲标题、播放状态或播放进度。`MediaFunctionsView` 目前只发送媒体虚拟键、屏幕亮度和键盘背光命令；项目没有媒体会话、歌词、HTTP 或定时刷新代码。不能在 `Draw()` 中执行网络请求、WinRT 媒体查询或阻塞等待。

## 推荐分层

```text
LyricsCoordinator (后台取消任务 + 当前歌曲快照)
        |
        +-- IMediaSession          -> 当前曲目/播放状态/进度
        +-- ILyricsProvider         -> 同步歌词或普通歌词
        +-- LyricsParser            -> LRC 时间戳解析
        +-- LyricsViewModel         -> 当前行、下一行、状态文本
        |
        +-- Action<LyricsSnapshot> -> TouchBar.UpdateSafely(...)
                                      -> LyricsView.SetSnapshot(...)
                                      -> OnInvalidate -> VSync
```

建议每个接口都带 `CancellationToken`，并在每次歌曲变化时取消上一次的歌词查询：

```csharp
internal sealed record MediaTrack(string Title, string Artist, string? Album,
    TimeSpan? Duration, TimeSpan Position, bool IsPlaying, string SourceId);
internal sealed record LyricsLine(TimeSpan? Start, string Text);
internal sealed record LyricsSnapshot(MediaTrack? Track, IReadOnlyList<LyricsLine> Lines,
    int CurrentLine, string State);
internal interface IMediaSession : IAsyncDisposable
{
    Task<MediaTrack?> ReadAsync(CancellationToken token);
    event Action? Changed;
}
internal interface ILyricsProvider
{
    Task<IReadOnlyList<LyricsLine>> GetAsync(MediaTrack track, CancellationToken token);
}
```

## 媒体会话

Windows 10 的首选是 `GlobalSystemMediaTransportControlsSessionManager`（Windows.Media.Control），因为它同时覆盖 Spotify、系统播放器、浏览器中支持媒体会话的站点，并能读标题、艺人、播放状态、播放位置和变化事件。当前 .NET 6 工程没有 Windows SDK/WinRT 投影依赖，需先增加与目标系统匹配的 `Microsoft.Windows.SDK.NET`（或在单独的 `MediaSession` 项目中封装 ABI）。不要用前台窗口标题作为正式曲目源；它在 Edge/Spotify 多窗口和本地文件时不稳定。

若 Windows SDK 投影不可接受，可先实现可替换的 `IMediaSession`：

- 当前前台播放器窗口标题作为低保真 fallback，仅用于标题检索，明确标记 `SourceId=foreground-title`。
- 不要把 `GetForegroundWindow()` 轮询直接写入 `LyricsView`，仍由 coordinator 去重、取消和节流。

## 歌词源与缓存

优先使用 LRCLIB 的 `GET https://lrclib.net/api/get?artist_name=...&track_name=...&album_name=...&duration=...`，响应包含 `syncedLyrics` 和 `plainLyrics`。`syncedLyrics` 用 LRC 解析，缺少时间戳时按普通歌词显示。请求必须设置有限超时（约 5 秒）、取消 token、User-Agent，并把曲目键标准化为 `artist\0title\0album\0durationSeconds`。

本地缓存以 SHA-256 曲目键为文件名，限制大小和条数；网络失败时先展示缓存，不能阻塞 Touch Bar。不得把歌词服务返回文本当成程序指令；仅解析 JSON 中的歌词字段。应在说明中注明第三方歌词服务和网络依赖，必要时提供 `lyrics.provider=none/local` 配置。

LRC 解析规则：支持 `[mm:ss.xx]`、`[mm:ss.xxx]`、同一行多个时间戳、`[offset:+/-N]`；忽略 `[ar]`、`[al]`、`[ti]` 等元数据。按时间排序，重复时间戳合并。当前行使用二分查找最后一个 `Start <= Position`，同步时钟每 100–250 ms 读取一次；只有当前行或进度发生可见变化时才重新提交帧。

## Touch Bar 视图

建议新增 `Views/LyricsView.cs`，继承 `TouchBarView`，不直接创建 HTTP/WinRT 线程。2008×60 上画：左侧小型返回/媒体图标，中间当前歌词（主字号约 25，单行省略），右侧可选下一句或“歌词/无歌词”状态。歌词过长时使用静态省略，避免滚动动画持续占用 USB/IP；如需滚动，使用 100 ms timer 并仅在文字变化后刷新。

歌词模式应有明确的进入和退出路径：

1. 在媒体栏增加一个固定宽度的“歌词”按钮；其 `OnTouchUp` 只发送 `ShowLyrics` 事件。
2. `TouchBar` 保留 `LyricsView` 实例，接收 coordinator 快照并在 `UpdateSafely` 中切换。
3. 歌词页左侧“媒体”按钮返回 `MediaFunctionsView`；切换时调用 `DisposeViewLastTouch()`，防止媒体长按命令残留。
4. 无曲目、暂停、无同步歌词或网络失败时显示状态文本，并保留返回媒体按钮。

若用户要求“播放就自动显示”，可在 `LyricsCoordinator` 首次获得 `IsPlaying=true` 且有歌词后自动切换；默认更安全的是点击“歌词”进入，避免每次切歌把用户正在使用的亮度/音量栏替换掉。这个行为应由 `settings.json` 的 `lyrics.autoShow` 控制，默认 false。

## TouchBar 生命周期

`TouchBar.Init(true)` 创建 coordinator 后启动后台循环；循环只更新快照，绝不直接调用 Skia。`Dispose` 顺序应先取消 coordinator、等待其任务退出，再解绑事件和释放传输；网络响应到达后必须检查 token/曲目键，防止旧歌词覆盖新歌曲。`TouchBar.UpdateSafely` 内只做轻量状态更新和 `Invalidate`，不要持有 `viewLock` 等待 HTTP。

建议默认刷新：媒体会话变化事件触发即时读取；另有 250 ms 位置轮询作为事件丢失 fallback；歌词 HTTP 每首歌最多一次并缓存。设置文件热加载时重建 provider 配置，不重启 USB/IP 传输。

## 验证计划

1. `LyricsParser` 单元检查：时间戳、offset、多时间戳、中文、空行和无同步歌词。
2. `LyricsView` 离线渲染 2008×60/2170×60，检查中文字形、按钮边界和长句省略；禁止打开 USB 设备。
3. `LyricsCoordinator` 使用 fake session/provider 验证切歌取消、旧响应丢弃、暂停和网络失败回退。
4. 真实机先用本地 `.lrc` fixture，再接 LRCLIB；验证当前歌词行随播放进度变化且 Touch Bar 触摸仍能返回媒体栏。
5. 网络不可用、歌词服务超时和播放器退出时必须回到可操作的媒体栏，不应让主程序停止。

## 许可证与发布

复制 GitHub 项目代码前先确认许可证；只复刻公开协议和数据格式，不直接复制 GPL/AGPL 文件到当前分发包，除非接受相应源代码和分发义务。歌词服务的内容许可与地区可用性应写入使用说明。建议先保留独立 `LyricsResearch`/`LyricsSession` 层，确认许可证后再合并到主项目。
