# Touch Bar：微软内置驱动绑定与恢复

**后续进展：本机已有可用 USBIP 显示方案。** 当前使用 `TouchBar-USBIP/Start-Media.cmd`，屏幕与触摸音量控制已实测。日常使用及恢复入口见 `../TouchBar-USBIP/使用说明.md`。下文是较早 WinUSB 方案的历史记录，其“当前”描述仅指当时快照。

本机已恢复 Apple 原版驱动，恢复于 2026-11-08 02:57:29 完成：`oem171.inf` / `HidUsb`、`AppleDFR` 过滤器、`Started=True;Problem=0`，无需重启电脑。

本次微软已签名驱动绑定和真实触摸输入测试通过，但显示传输失败。应用和独立原生缓冲区探针均在 GINF 写入完成时返回错误 998，实际传输 0 字节。因此当前 WinUSB 显示方案不能正常使用，自定义按钮界面未通过验收。

## 已验证结果

目标机器为 MacBookPro16,1，Touch Bar 根实例为 `USB\VID_05AC&PID_8302\0000000000000000`。下表为恢复 Apple 驱动之前的历史测试结果，不是当前绑定状态：

| 设备 | 已绑定系统 INF / 服务 | 结果 |
| --- | --- | --- |
| Touch Bar 根设备 | `usb.inf` / `usbccgp` | Started，Problem=0 |
| 触摸接口 MI_00 | `input.inf` / `HidUsb` | Started |
| 显示接口 MI_01 | `winusb.inf` / `WINUSB` | Started，Problem=0 |

- 根设备通过原版 `C:\Windows\INF\usb.inf` 的 `Composite.Dev` 模型完成跨类别选择；MI_01 由 Unknown 类通过普通选择成功绑定 `WINUSB` 模型。两次安装均返回 `NeedReboot=False`。
- `usbccgp.sys`、`WinUSB.sys` 签名有效；两个驱动候选都带有 Windows 的 `DNF_INBOX_DRIVER` 和 `DNF_INF_IS_SIGNED` 标志。
- 根设备始终保留 `OriginalConfigurationValue=1`；测试期间移除了根设备的 `AppleDFR` 过滤器，结束时已恢复。MI_01 显示接口 GUID 为 `{2003cacd-9e7c-477c-ab06-a5a8bbb1a63e}`。
- 物理触摸采样收到 169 份报告，其中 115 份为活动触摸、42 次释放；有效 X 坐标为 2645–32712，报告布局与程序解析匹配。这确认输入数据通路，不等同于按钮动作已经验收。
- 测试时显示接口可枚举。独立探针使用 `AllocHGlobal` 原生缓冲区，并在垃圾回收后核对请求数据，依然出现提交错误 997（异步等待）、完成错误 998、`STATUS_ACCESS_VIOLATION (0xC0000005)`、传输 0 字节。这说明更换应用缓冲区分配方式未解决故障；具体根因尚未确认，不能据此断定某个 Apple 驱动有问题。
- 显示握手、首帧确认、实际按钮动作及应用切换均未通过验收，已结束本轮自定义模式测试并恢复 Apple 驱动。

证据位于 `verification-20261108`：`after-signed-driver-state.json`、`bind-Composite.log`、`bind-WinUSB.log`、`repaired-package-flags.json`、`winusb-physical-touch.json`、`display-native-buffer-probe.jsonl`、`bind-Restore.log`。`after-signed-driver-state.json` 是恢复前的测试快照；最终恢复见 `bind-Restore.log`，实时状态由下面的只读命令查询。

## 查看当前状态

使用本机 PowerShell 7，无需修改脚本执行策略：

```powershell
$repo = (Get-Location).Path
& pwsh.exe -NoProfile -File (Join-Path $repo 'tools\TouchBarDriver.ps1') -Plan
```

`TouchBarDriver.ps1` 的 `Status` / `Plan` 始终只读。Plan 中的验证标志记录本次已完成的历史检查；`Devices` 和 `CurrentBinding` 是运行时查询。当前应为 `AppleOriginal`。`DisplayHandshakeVerified` 和 `CustomModeReady` 为 false。

## 保留的实验绑定入口

当前保留 Apple 原版驱动用于正常使用。下面命令仅记录已验证的绑定入口，不是可用自定义显示方案的安装建议；重复绑定不能解决已复现的显示传输错误。

实际绑定入口为 `tools\BindTouchBar.ps1`，它仅适用于这台已备份的机器。若后续有明确修复需要重新测试，应在**管理员 PowerShell 7**中依次执行，每步成功后再继续：

```powershell
& '.\tools\BindTouchBar.ps1' -Mode Composite
& '.\tools\BindTouchBar.ps1' -Mode WinUSB
```

脚本依赖 `verification-20261108\before-device-properties.json` 中的原始过滤器记录，以及系统保留的 Apple `oem171.inf`。它检查具体设备和候选模型，并记录安装日志；Composite 步骤失败时会尝试恢复 Apple 绑定。此脚本不是供其他机器直接运行的通用安装器，也不应在已经启用 Composite 的状态下重复执行第一步。

不能使用 `pnputil /add-driver C:\Windows\INF\winusb.inf /install` 代替模型绑定：本机 MI_01 的兼容 ID 不直接匹配 `USB\MS_COMP_WINUSB`，添加驱动包成功不代表设备已经绑定。

## 恢复 Apple 原版功能

本轮已经执行恢复，无需重复操作。以后从实验模式恢复时，先运行 `TouchBar-Custom\Stop.cmd` 退出自定义程序，再在**管理员 PowerShell 7**中执行：

```powershell
& '.\tools\BindTouchBar.ps1' -Mode Restore
```

此命令将根设备切回 Apple `oem171.inf` / `AppleDFR_Device`，恢复备份记录中的完整 `LowerFilters`，并仅重启 Touch Bar 根设备。本轮最终恢复已确认 `HidUsb`、`AppleDFR` 过滤器和 `Started=True;Problem=0`，安装返回 `NeedReboot=False`。设备状态正常与实际按键验收应分别确认。

Restore 恢复根设备绑定和原始过滤器；不会删除 MI_01 上的自定义 GUID，也不会删除驱动包。根设备回到 Apple 模式后，MI_01 自定义接口不再处于连接状态。原始 `OriginalConfigurationValue=1` 在本次切换中没有更改。

Apple 驱动导出备份保留在 `backup-20261108\AppleDriver`。如自动恢复报错，应保留日志并检查，不要整项导入旧设备注册表或手写 `Driver`、`ClassGUID`、`Service` 等系统管理属性。

## 本机驱动包修复记录

最初安装失败 `0xE000022F` 的原因是系统已登记的 USB 驱动存储目录缺少原版 `usb.inf`。已从对应 WinSxS 组件恢复相同文件，并核对 SHA256 与 `C:\Windows\INF\usb.inf` 一致；之后发布名与驱动存储路径双向查询正常，原版 INF 成功绑定。本次没有修改 INF 内容、设备硬件 ID、签名策略或安全启动配置。WinUSB 驱动包完整，无需修复。
