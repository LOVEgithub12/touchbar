# 贡献指南 / Contributing

欢迎提交可复现的问题和范围清楚的改动。提交前请先阅读首页与[开发说明](docs/DEVELOPMENT.md)。

Reproducible reports and focused changes are welcome. Read the README and [development guide](docs/DEVELOPMENT.md) first.

## 问题报告 / Bug reports

请提供 / Include:

- 机型、面板宽度、Windows 版本、当前提交 SHA。 / Model, panel width, Windows version and commit SHA.
- 预期行为、实际行为、最短复现步骤。 / Expected/actual behavior and minimal reproduction steps.
- 使用的页面、播放器、输出模式，以及是否仅提权窗口下发生。 / Page, player, audio output mode and whether an elevated foreground window is required.
- 相关错误片段；先清除歌曲信息、用户目录、序列号和凭据。 / Relevant error excerpts, with song metadata, user paths, serials and credentials removed.

不要上传完整工作目录、播放器 profile、私人媒体或未经清理的诊断包。

Do not upload whole workspaces, player profiles, private media or unsanitized diagnostic bundles.

## 修改与验证 / Changes and validation

1. 新建分支，保持变更聚焦；涉及用户行为时同步中英文说明与更新记录。 / Use a branch and keep changes focused; update both language guides and the changelog for user-visible behavior.
2. 执行 `pwsh -File .\tools\Check.ps1`，说明检查环境与结果。 / Run `pwsh -File .\tools\Check.ps1` and report the environment and result.
3. UI 修改附真实生产视图截图或动画，注明合成输入或实物拍摄。 / For UI changes, attach production-view screenshots/animations, labeled as synthetic-input renders or hardware captures.
4. 涉及驱动、触摸、背光、启动时，分别列出实机验证与未验收部分。 / For driver, touch, backlight or startup changes, distinguish hardware validation from pending acceptance.
5. 不提交 EXE/DLL、安装包、个人日志、歌曲、桥接状态 JSON、备份或大批临时帧。 / Do not commit binaries, installers, personal logs, songs, bridge state, backups or temporary frame batches.

PR 描述需说明触发条件、改前/改后行为、验证和已知限制。不要将历史驱动研究流程重新当作当前推荐安装方法。不要为发布本身修改用户设备的安全策略。

Describe the trigger, before/after behavior, validation and known limits in pull requests. Do not reintroduce historical driver research as the recommended setup, or change a user's security configuration merely to publish code.

本仓库存在多种来源与授权状态，贡献前阅读[第三方与授权说明](THIRD_PARTY_NOTICES.md)。保留引用来源与原许可证，不替来源不明确的文件补造授权。

This repository contains components with different origins and licensing status. Read [Third-party notices](THIRD_PARTY_NOTICES.md), retain attribution and existing licenses, and do not invent permission for files with unclear provenance.
