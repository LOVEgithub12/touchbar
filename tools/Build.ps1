[CmdletBinding()]
param([string]$DotNetPath = 'dotnet', [switch]$PrepareRuntime)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$source = Join-Path $root 'WindowsTouchBar-master\WindowsTouchBar'
& $DotNetPath build (Join-Path $source 'WindowsTouchBar.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Application build failed.' }
if ($PrepareRuntime) {
    $destination = Join-Path $root 'TouchBar-USBIP\app'
    $executable = Join-Path $destination 'WindowsTouchBar.exe'
    $running = @(Get-Process -Name WindowsTouchBar -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $executable })
    if ($running.Count) { throw 'Stop the existing app with Stop.cmd before preparing its runtime.' }
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    foreach ($item in Get-ChildItem -LiteralPath (Join-Path $source 'bin\Release\net6.0-windows')) {
        if ($item.Name -eq 'settings.json' -and (Test-Path -LiteralPath (Join-Path $destination 'settings.json'))) { continue }
        Copy-Item -LiteralPath $item.FullName -Destination $destination -Recurse -Force
    }
    Write-Output "Runtime prepared in $destination (existing settings preserved)."
}
