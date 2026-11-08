# 文章 CKS 方法在本机的适用性核对

核对日期：2026-11-08。只进行了文档阅读和本机只读检查；未修改固件密钥、EFI 分区、Windows 启动配置、代码完整性策略或设备驱动。

## 结论

不能直接按文章执行。本机未满足文章所需的、已验证可操作的自定义 UEFI Platform Key 导入前提。管理员权限可以读取固件状态，但不能替代固件提供的密钥管理能力。没有把清空 PK、修改 SetupType 或复制 SiPolicy.p7b 当作试探手段。

## 已验证状态

| 项目 | 结果 |
| --- | --- |
| 机器 | MacBookPro16,1，Apple T2 / iBridge 固件 |
| Windows | Windows 10 Pro 22H2，19045.6093 |
| CustomKernelSigners 产品策略 | 存在，DWORD 0，未启用 |
| 旧 CodeIntegrity-AllowConfigurablePolicy 策略 | 不存在，NTSTATUS 0xC0000034 |
| Windows SetupType | 0，正常状态 |
| 固件 SecureBoot | 01，开启（管理员 Get-SecureBootUEFI 实测） |
| 固件 SetupMode | 00，非 Setup Mode（管理员实测） |
| 固件标准 PK 变量 | 当前未定义，0xC0000100（管理员实测） |

原始只读结果：CksReadOnlyStatus.json、FirmwareReadOnlyStatus.json。PK 读取没有输出任何证书或私钥数据。

## 文章要求与本机差异

论坛文章使用 CKS：自行创建内核签名证书和平台密钥，把自己的 PK 导入固件，再用对应私钥签署内核信任策略并放入 EFI 分区，最后通过 ssde.sys 持久化 CKS。它不是给某个驱动文件增加普通数字签名即可完成的安装方式。

其上游项目明确列出前提：You are able to set UEFI Platform Key。

Apple 官方 T2 文档说明启动安全策略由 recoveryOS 中的启动安全性实用工具管理，提供 Full Security、Medium Security、No Security 三种模式。文档没有提供文章所需的自定义 PK 导入或 UEFI Setup Mode 密钥管理流程。T2 的 Windows 信任链使用固件内置的 Microsoft Windows Production CA 2011。

本机同时返回 SecureBoot=1、SetupMode=0、PK 未定义，不能把它当作普通 PC 的可管理标准 PK 环境。读取结果本身不能证明所有非官方技术路径都不可能，但足以说明文章的前提尚未满足；没有已验证的导入和恢复路径，不能直接部署文章的全局信任策略。

旧 EnableCKS.exe 还会尝试启用本机已经不存在的旧策略。文章自身说明这种情况可能导致反复重启，并推荐另一个版本的工具。换工具不会解决 T2 平台密钥前提。

## 与 Touch Bar 修复的关系

本报告只读核对完成后，另行进行了已获授权的微软内置驱动绑定验证：USB Composite / WinUSB 安装和真实触摸输入通过，但显示请求以 Win32 998 / NTSTATUS 0xC0000005 失败，已恢复 Apple 原版驱动。TouchBar-Custom 使用 WinUSB，不依赖未签名 DFRDisplayKm.sys；本次验证始终没有部署 CKS 或关闭安全启动。详情见 ../verification-20261108/验证结果.md。

## 来源

- 用户提供的文章：https://www.52pojie.cn/thread-1971554-1-1.html
- CKS 上游项目及前提：https://github.com/HyperSine/Windows10-CustomKernelSigners
- Apple T2 启动过程：https://support.apple.com/zh-cn/guide/security-pdf/sec5d0fab7c6/web
- Apple T2 启动安全选项：https://support.apple.com/zh-cn/guide/security-pdf/secc7b34e5b5/1/web/1
