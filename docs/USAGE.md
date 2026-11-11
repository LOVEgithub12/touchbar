# 功能、操作与故障排查

[English](USAGE.en.md) · [首页](../README.md) · [安装](INSTALL.md)

## 页面与触摸

| 场景 | 操作 / 结果 |
|---|---|
| 媒体主栏 | 左侧 F / 1–12，随后屏幕亮度、键盘背光、媒体三键、音量；主栏整体向右微调 20 px |
| F 键页 | 点击主栏左侧图标进入；F1–F12 等宽排列，最左侧返回媒体栏 |
| 播放歌词 | 匹配到活跃 foobar2000 状态后使用歌词页；保留媒体三键、播放器入口、静音与音量 |
| 暂停 / 停止 | 离开歌词页；配置的前台应用快捷栏可接管，否则显示普通主栏 |
| 播放时切浏览器 | 继续保留媒体/歌词上下文，浏览器不会抢走正在使用的播放页面 |
| 临时调亮度 / 背光 | 点击歌词页播放器图标回主栏；播放期间空闲 4 秒自动回歌词，计时从最后一次实际释放触摸开始 |
| 按住按钮 | 音量、亮度等允许长按连发；静音保持单次切换；释放/销毁会结束命令 |
| 滑动和误触 | 页面切换与背景切换要求有效的完整点击；滑入控件或多余释放不会触发第二次动作 |

显式 F 页的状态由应用管理，已不依靠“Fn 松开事件是否收到”。这消除了此前 Fn 推断状态残留导致一直停在 F1–F12 的路径。旧 Fn 推断 helper 和测试仍作为历史参考保留。

![媒体主栏](images/media.png)
![F1–F12 页](images/function-keys.png)

## 歌词与背景

歌词桥接每 250 ms 输出播放器状态；渲染端使用单调时钟插值，在每帧评估歌词时间轴，避免直接按桥接轮询频率跳字。播放跳转、切歌和换行会重置相关状态。

优先匹配曲目旁的同名 LRC，再读取内嵌时间戳歌词，最后查找 profile 歌词目录。会核对标题/艺人，存在冲突或多个不一致候选时不盲目采用。LRC 支持常规行时间、多时间标签、offset 和增强逐字标签。普通行时间可估算行内着色，只有逐字标签能提供来源明确的逐字时间。

歌词单行显示，已唱部分紫蓝渐变，未唱部分白色；默认 34 px、必要时 28 px，再按演唱前沿平滑滚动。无下一行预览和底部进度条。混排使用已安装字体回退，系统缺少对应文字字体时仍需安装字体。

**点击歌词文字区域**按下面的顺序循环，松开后生效，偏好保存到本地：

| 模式 | 视觉行为 |
|---|---|
| 极光绸带 Aurora | 紫、蓝、青光带，较柔和的能量包络；中间明显、上下淡细，边缘渐隐；星点与两侧星尘保留 |
| 实时频谱 Spectrum | 真实共享混音 FFT 驱动的宽频谱，较快的主体响应；多层散点具有不同高度、延迟、悬停和淡出，不是整层横向平移 |
| 关闭 Off | 去除音乐背景，保留歌词与媒体操作；不是关闭屏幕或隐藏歌词 |

两种背景都保留两侧星尘脉动。侧边效果的底部受媒体按钮原底边约束；启用背景时，按钮背景只缩短底部 6 px，顶部和触摸区域不变。歌词页持续请求全亮，避免出现周期性变暗。

### 音频来自哪里

- WASAPI 共享输出回环；保存派生能量和频谱，不保存原始 PCM，不录制麦克风。
- 512 个 32 Hz–约 16.5 kHz 对数频带；低频使用 4096 点窗，中高频使用 2048 点窗，结合各声道功率避免反相抵消。
- 分析约 60 Hz 更新；传输与渲染最终帧率还受设备应答与系统负载影响，不能把 60 Hz 分析目标视为硬件帧率保证。
- 全端点混音不等同于人声分离。伴奏继续时仍有律动；真正静音会衰减，已发射粒子可短暂滞留后消失。
- 独占 WASAPI / ASIO 可能绕开共享混音。要使用频谱，请选择可被共享回环捕获的输出方式。

## 自定义快捷栏

编辑**运行目录** `TouchBar-USBIP\app\settings.json`，应用会监测配置变化。源码中的 `settings.json` 是初始模板；构建助手保留运行目录已有配置。

示例：为 Chrome 配置返回和新标签页按钮：

```json
{
  "views": [
    { "type": "media", "default": true },
    { "type": "function" },
    {
      "type": "custom",
      "application": "chrome.exe",
      "components": [
        { "type": "button", "title": "Back", "keyCodes": ["LEFT"], "modifierKeyCodes": ["MENU"], "width": 150 },
        { "type": "spacer" },
        { "type": "button", "title": "New tab", "keyCodes": ["VK_T"], "modifierKeyCodes": ["CONTROL"], "width": 200 }
      ]
    }
  ]
}
```

进程名称需要与实际前台应用匹配；按键名使用 `WindowsInput.Native.VirtualKeyCode` 枚举。也可用 `icon` 加 Segoe MDL2 Assets 字符。播放优先规则仍适用，暂停后再测试浏览器快捷栏。Explorer“确定/取消/应用”支持只覆盖识别到原生 BUTTON 控件的 Explorer 对话框，不是所有 Windows 对话框。

## 故障排查

| 现象 | 检查顺序 |
|---|---|
| 提示缺少程序 | 运行 `tools\Build.ps1 -PrepareRuntime`；确认 `TouchBar-USBIP\app\WindowsTouchBar.exe` 存在 |
| 提示找不到 pwsh | 安装 PowerShell 7，并重新打开会话，确认 `Get-Command pwsh` |
| 黑屏 / 启动失败 | 看 `launcher.log` 和 `app\WindowsTouchBar.status.log`；检查 usbipd 服务、确切设备、是否已绑定 VBoxUSB，避免重复启动 |
| 开机设备 Code 37 | 对照设备管理器与恢复日志；仅已确认对应故障时使用安装指南的开机恢复工具 |
| 中文 USB 设备导致登录启动失败 | 本版已用显式 UTF-8 读取 usbipd JSON，确认启动脚本与 helper 一起更新，不能只替换 EXE |
| 点击 Stop 后又自动出现 | 确认 `Stop.ps1`、`AutoStart.ps1` 与 CMD 入口同版；停止应取消初始化中的 helper，而不仅结束主进程 |
| 触摸有反馈但按键没作用 | 确认应用由普通桌面用户启动；目标程序若提权，普通按键受 UIPI 限制 |
| 任务管理器里音量无效 | 本版单独音量命令走 Shell 系统命令；确认新 `NativeVolumeControl` 已编译，普通多键快捷方式不走这条路径 |
| 歌词不显示 / 不换曲 | 检查桥接输出 app 路径、`foobar-state.json` 是否持续刷新、LRC 时间/元数据是否匹配；移动仓库后重新配置桥接 |
| 播放中浏览器不换栏 | 这是设计行为；**暂停后**测试前台浏览器切栏，并确认 `settings.json` 有对应进程 |
| 频谱不动 | 确认未切到 Off、当前是播放状态、实际输出可被共享回环捕获；独占/ASIO 尝试改为共享输出 |
| 某语言显示方框 | 安装系统相应语言字体；运行 FontChecks 可检查当前系统字形覆盖 |

主要本地文件：

| 文件 | 用途 |
|---|---|
| `TouchBar-USBIP\launcher.log` | 手动启动结果 |
| `TouchBar-USBIP\autostart.log` | 登录等待、重试与取消 |
| `TouchBar-USBIP\app\WindowsTouchBar.status.log` | 传输初始化、尺寸和首帧应答 |
| `TouchBar-USBIP\app\WindowsTouchBar.error.log` | 应用异常（如有） |
| `TouchBar-USBIP\app\foobar-state.json` | 本地播放器状态，可能含歌曲与文件路径 |
| `TouchBar-USBIP\app\visualizer-preference.json` | 背景选择 |
| `%ProgramFiles%\TouchBar-Startup\boot-recovery.log` | 独立系统恢复任务日志（安装后） |

提交 issue 前去掉日志中的个人路径、歌曲元数据和设备序列号；提供系统、机型、提交版本、复现步骤和对应错误片段即可。
