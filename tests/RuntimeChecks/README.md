# Runtime regression checks

These checks link the application's actual UI and touch-coordinate source files. They do not open a USB device or send keyboard input. A counting command replaces keyboard commands in the press/release tests; layouts are rendered into offscreen Skia bitmaps.

Build the main application first (`pwsh -File tools/Build.ps1` from the repository root). Then, from this directory, run with the installed .NET 6 SDK:

```powershell
$dotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
& $dotnet run --project RuntimeChecks.csproj --configuration Release
```

The project uses the application's existing local managed dependencies and native x64 Skia library, so it needs no test framework or additional NuGet package. Rendered PNGs are saved under the executable's `artifacts` directory. These checks verify application behavior only; they cannot confirm USB communication, physical touch input, brightness control, or driver compatibility.

For the complete offline suite and production-rendered documentation previews, see [Development](../../docs/DEVELOPMENT.md).
