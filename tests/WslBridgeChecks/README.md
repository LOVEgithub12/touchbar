# WSL host regression checks

Run with the local .NET 6 SDK:

```powershell
$dotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
& $dotnet run --project 'tests\WslBridgeChecks\WslBridgeChecks.csproj' -c Release
```

The tests launch a second instance of the test executable as a fake protocol peer. They do not start WSL, access USB/HID devices, send keys, or install drivers.

They exercise real stdin/stdout/stderr pipes, RGBA payload contents, fragmented acknowledgements, two consecutive frame identifiers, touch delivery during frame waits, stale acknowledgement rejection, missing-acknowledgement timeout, and geometry/touch bounds.

Production starts `wsl.exe -d TouchBarBridge -u root --exec /opt/touchbar/t2-touchbar-bridge` only when the user requests `--wsl`. USB attachment must be established separately. `--wsl --check` validates READY, the touch-interface ready marker, and the first exact frame acknowledgement, then exits; actual visual output and touch-button behavior still require hardware verification.
