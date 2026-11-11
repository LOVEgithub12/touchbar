# Installation and updates

[简体中文](INSTALL.md) · [Home](../README.en.md) · [Usage and troubleshooting](USAGE.en.md)

## 1. Requirements and validated scope

| Item | Requirement / validation |
|---|---|
| Tested hardware | MacBookPro16,1, Apple T2, 2008 × 60 Touch Bar |
| Tested OS | Windows 10 Pro 22H2, build 19045; Windows 11 hardware acceptance is pending |
| USB identity | Scripts currently require `USB\VID_05AC&PID_8302\0000000000000000`, rather than binding arbitrary USB devices |
| Boot Camp | Working Apple device support; brightness/backlight depend on the corresponding interfaces |
| Build | [.NET 6 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/6.0), validated with 6.0.428; a runtime alone cannot compile |
| Scripts | [PowerShell 7](https://github.com/PowerShell/PowerShell/releases), with `pwsh.exe` on PATH; Windows PowerShell 5.1 supports the login/recovery paths |
| Transport | Official x64 [usbipd-win 5.3.0](https://github.com/dorssel/usbipd-win/releases/tag/v5.3.0); compatibility with arbitrary later command contracts is not established |
| Optional lyrics | Existing foobar2000 foobox/JSplitter setup; the automatic installer targets the profile layout below |
| Optional checks | Node.js 22 for offline bridge checks |

This is a source project validated on an existing hardware setup, rather than a universal Boot Camp installer. The current route requires neither WSL, disabling Secure Boot nor enabling test signing. Historical driver references in this repository are not part of these instructions.

## 2. Build the source

In PowerShell 7, from the repository root:

```powershell
git clone https://github.com/LOVEgithub12/touchbar.git
cd touchbar
.\tools\Build.ps1 -PrepareRuntime
```

For a user-local SDK:

```powershell
.\tools\Build.ps1 -DotNetPath "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" -PrepareRuntime
```

`Build.ps1` restores NuGet packages and builds to `WindowsTouchBar-master\WindowsTouchBar\bin\Release\net6.0-windows`. `-PrepareRuntime` copies the output to the launcher's `TouchBar-USBIP\app` directory, preserving an existing `settings.json`. It refuses to replace an application running from that path. An EXE is not checked into this source distribution.

The application needs the .NET 6 Windows Desktop x64 runtime, normally included when building locally with the Windows .NET SDK. Use `dotnet --list-sdks` and `dotnet --list-runtimes` to inspect what is actually installed.

## 3. Configure USB/IP (administrator setup)

### 3.1 Install the official transport

Download **`usbipd-win_5.3.0_x64.msi`** from the official release linked above and place it in `tools\downloads\`. The installer is not redistributed here.

From an **administrator PowerShell 7** at the repository root:

```powershell
.\tools\InstallTouchBarUsbIp.ps1
```

The script verifies the Authenticode publisher and this fixed SHA-256:

```text
1C984914AEC944DE19B64EFF232421439629699F8138E3DDC29301175BC6D938
```

It installs Server/Drivers and disables the installer's `usbipd` LAN inbound firewall rule, since this client connects only to `127.0.0.1:3240`. If usbipd already exists, it stops rather than replacing it; inspect the version and service first. Public port exposure is unnecessary for local use.

### 3.2 Bind the Touch Bar

Still in administrator PowerShell 7:

```powershell
.\tools\BindTouchBarUsbIp.ps1 -Mode Bind
```

The script obtains the current bus ID from usbipd and verifies the exact device identity. Do not hard-code a potentially changing ID such as `1-6`. Binding hands the display device to VBoxUSB. Stopping this app does not automatically restore Apple's display service.

### 3.3 Launch as a normal user

Close the elevated terminal and double-click the root `Start-Media.cmd`, or run it in a normal desktop-user session:

```powershell
.\Start-Media.cmd
```

The launcher checks device binding and application readiness. If initial binding is incomplete it can request elevation for that step separately. The main app must run in the normal desktop session; running the entire app as administrator is not a general troubleshooting fix.

For diagnostics, run `Stop.cmd` first, then `TouchBar-USBIP\Check.cmd`. Inspect `TouchBar-USBIP\app\WindowsTouchBar.status.log`. A successful running instance reports `RUNNING: ... first frame acknowledged`. `Check` briefly accesses the device; it is not an offline unit test.

## 4. Configure foobar2000 lyrics (optional)

Media and function pages do not need the lyrics bridge. The automatic installer targets an existing setup at:

```text
C:\Program Files\foobar2000\profile\foobox\script\js_panels\bottombar.js
C:\Program Files\foobar2000\profile\foobox\script\js_common\
```

Back up and close/unload the playback panel, then use an administrator PowerShell:

```powershell
.\tools\InstallFoobarTouchBarBridge.ps1
```

The script backs up the original panel, installs `touchbar_bridge.js`, appends its include once, and embeds the **current repository's absolute app path** in the installed copy. Reload the JSplitter panel or restart foobar2000, then play a track with timestamped lyrics. Personal playback state remains in local `app\foobar-state.json` and should not be committed.

Do not use the fixed-profile installer unchanged for other profile layouts. Follow the [bridge protocol](../tools/LyricsResearch/FOOBAR-BRIDGE.md) to set `TouchBarBridgeOutputPath`, copy the bridge and add its include manually. Reconfigure its path if the repository is moved.

The app supports matching local `.lrc`, profile lyric files and embedded timestamped lyrics; it does not fetch lyrics online. See [Usage](USAGE.en.md#lyrics-and-backgrounds).

## 5. Login startup and recovery (optional)

Run root `Enable-Autostart.cmd` as a normal user to configure **HKCU Run**, or `Disable-Autostart.cmd` to disable it. This starts **after user login**, not as a display service before login. The helper waits for usbipd/device readiness, retries within a limit and supports cancellation through Stop.

For the confirmed VBoxUSB boot failure Code 37 / `C0000034`, an additional administrator installer is available:

```powershell
.\tools\InstallTouchBarBootRecovery.ps1
```

It copies the recovery script into protected `%ProgramFiles%\TouchBar-Startup` and installs a SYSTEM boot task, so a privileged task never executes user-writable repository code. Use it for that matching failure, rather than as a substitute for setup diagnosis. A working existing task does not need reinstallation.

UTF-8 device-state parsing and startup cancellation have been tested. Another full hardware reboot after the latest fixes remains pending. After relocating the repository, reconfigure login startup and the bridge path.

## 6. Updating and restoring

For an existing installation:

```powershell
.\Stop.cmd
git pull --ff-only
pwsh -NoProfile -File .\tools\Build.ps1 -PrepareRuntime
.\Start-Media.cmd
```

Keep copies of your `settings.json` and `visualizer-preference.json`. Commit or back up local source changes and resolve merges normally before building; do not discard personal configuration with a forced reset.

To restore Apple's display driver, stop the app and run `TouchBar-USBIP\Restore-Apple.cmd`, accepting the administrative prompt for the driver operation. Restoring the driver and stopping the application are separate actions. The tool checks the resulting device state. See [Troubleshooting](USAGE.en.md#troubleshooting).

To remove the lyrics bridge, remove only the include you added and reload the panel. Do not overwrite unrelated later panel edits with a whole old backup. Disable startup before removing your runtime directory. Keep usbipd installed if other software uses it.
