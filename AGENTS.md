# Working on TouchBar

Read [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) before changing code. Game setup and metric definitions are in [docs/GAMING.md](docs/GAMING.md). If the ignored local `docs/AI_HANDOFF.md` exists, consult its latest dated sections for machine-specific deployment history; historical snapshots are not current process state.

- Preserve existing edits, local configuration, research and runtime files. Inspect Git status before changing or staging files.
- The current transport is usbipd-win plus the local C# USB/IP client. Run the main app as an ordinary desktop user. Do not change drivers, security settings or power plans as part of routine source work.
- Keep explicit F-page selection, playback priority over browser shortcuts, the Aurora → Spectrum → Off cycle, and the four-second temporary-page return after touch release.
- Build with `tools/Build.ps1`; run offline checks with `tools/Check.ps1`. Both accept a custom `-DotNetPath`. An installed runtime alone is not an SDK.
- `TouchBar-USBIP/app/` is the deployed runtime, separate from Release build output. Building alone does not deploy. Check the running executable, fresh status log and DLL hash before asserting a version is loaded.
- CPU/GPU bridge mode uses one TrafficMonitor snapshot and no independent hardware collector. PresentMon remains separate for FPS. Preserve native sensor semantics and missing values.
- Never enumerate HardwareMonitor item metadata inside periodic bridge callbacks; its shared string storage is also used by the UI thread. See the bridge README and ABI checks.
- Keep personal game paths, songs, lyrics, original reference videos, telemetry dumps, local handoffs and compiled binaries out of commits. Stage publication files explicitly.
- Report source, build, deployment and GitHub publication separately. Offline checks and offscreen previews do not establish hardware acceptance. Update the local handoff when present, without publishing private machine history.
