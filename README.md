# Windows Touch Bar for Apple T2

Windows Touch Bar controls for Apple T2 MacBook models, with a local USB/IP display transport, media controls, brightness and keyboard-backlight integration, foobar2000 lyrics, and an animated aurora background.

## Source layout

- `WindowsTouchBar-master/WindowsTouchBar/` - .NET 6 Windows application.
- `tests/` - offline rendering, transport, lyrics, layout, and Fn inference checks.
- `tools/` - deployment, diagnostics, bridge, and research utilities.
- `TouchBar-USBIP/` - user-facing launch and configuration scripts. The live `app/` folder is intentionally excluded from source control.
- `DFRDisplayKm-master/` - upstream display driver reference sources.

## Build

Use the user-level .NET 6 SDK installed under `%LOCALAPPDATA%`:

```powershell
$dotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
& $dotnet build `
  'WindowsTouchBar-master\WindowsTouchBar\WindowsTouchBar.csproj' `
  --no-restore -c Release
```

The project targets `net6.0-windows`. The application depends on the packages listed in its project file and on a working Apple Touch Bar USB/IP setup.

## Checks

```powershell
$dotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
& $dotnet run --project 'tests\RuntimeChecks\RuntimeChecks.csproj' --no-restore -c Release
& $dotnet run --project 'tests\FnKeyChecks\FnKeyChecks.csproj' -c Release
```

The Fn service uses Apple KeyManager activity because the installed Apple driver does not expose the physical Fn state to user mode. The implementation includes filtering, manual reset, and an inactivity safety reset; it does not change driver settings.

## Runtime

The scripts under `TouchBar-USBIP/` assume `usbipd-win` and the signed VBoxUSB transport are already configured. Run them from an ordinary desktop-user session so touch-generated keyboard input is not blocked by UIPI. Driver binding and boot-recovery installation are separate administrative operations.

This repository does not include local logs, device snapshots, generated verification images, compiled binaries, downloaded installers, or machine-specific handoff archives.
