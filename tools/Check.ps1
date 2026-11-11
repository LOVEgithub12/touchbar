[CmdletBinding()]
param([string]$DotNetPath = 'dotnet', [string]$NodePath = 'node')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'Build.ps1') -DotNetPath $DotNetPath
foreach ($name in @('RuntimeChecks','LyricsChecks','NavigationChecks','PlaybackClockChecks','LyricsViewportChecks','FontChecks','FnKeyChecks','UsbIpHostChecks')) {
    & $DotNetPath run --project (Join-Path $root "tests\$name\$name.csproj") -c Release
    if ($LASTEXITCODE -ne 0) { throw "$name failed." }
}
$windowsPowerShell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
foreach ($name in @('StartupChecks','AutostartLifecycleChecks')) {
    & $windowsPowerShell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $root "tests\$name.ps1")
    if ($LASTEXITCODE -ne 0) { throw "$name failed." }
}
& $NodePath (Join-Path $root 'tools\LyricsResearch\Test-FoobarBridge.cjs')
if ($LASTEXITCODE -ne 0) { throw 'Foobar bridge checks failed.' }
Write-Output 'All offline checks passed. Hardware acceptance remains a separate step.'
