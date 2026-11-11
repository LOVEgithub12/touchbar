# WindowsTouchBar application source

[中文项目首页](../README.md) · [English project home](../README.en.md)

此目录保存当前 C# 应用及其历史工程结构。**当前安装方式使用本机 USB/IP**，详见[安装指南](../docs/INSTALL.md)。无需关闭 Secure Boot、启用 Windows 测试签名或安装旧 DFRDisplayKm 驱动。早期上游的驱动安装建议不适用于当前验证路线。

This directory contains the current C# app within its historical project layout. **The current installation uses local USB/IP**; follow the [installation guide](../docs/INSTALL.en.md). Disabling Secure Boot, enabling Windows test signing and installing the historical DFRDisplayKm driver are not required by this validated route. Earlier upstream driver instructions do not apply to this setup.

从仓库根目录构建 / Build from the repository root:

```powershell
pwsh -NoProfile -File .\tools\Build.ps1
```

来源与授权见 [THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md)。 / See the notices for origins and licensing scope.
