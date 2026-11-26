[CmdletBinding()]
param([string]$RuntimeDirectory = (Join-Path $PSScriptRoot '../../TouchBar-USBIP/app'))
$ErrorActionPreference='Stop'
$runtime=(Resolve-Path -LiteralPath $RuntimeDirectory).Path
$app=@(Get-Process -Name WindowsTouchBar -ErrorAction SilentlyContinue | Where-Object Path -eq (Join-Path $runtime 'WindowsTouchBar.exe'))
if ($app.Count -ne 1) { throw 'Start exactly one Touch Bar application first.' }
$collector=Join-Path $runtime 'Telemetry/TouchBarSensors.exe'
if (!(Test-Path -LiteralPath $collector)) { throw 'Sensor collector is missing; build it first.' }
$parentId=$app[0].Id
$existing=@(Get-CimInstance Win32_Process -Filter "Name='TouchBarSensors.exe'" | Where-Object {$_.ExecutablePath -eq $collector -and $_.CommandLine -match '--serve' -and $_.CommandLine -match "--parent $parentId( |$)"})
if ($existing.Count) { Write-Output "Session collector already running for Touch Bar PID $parentId."; return }
# Interactive, opt-in action only. No service, scheduled task, login entry, or main-app elevation.
$process=Start-Process -FilePath $collector -ArgumentList @('--serve','--parent',$parentId) -Verb RunAs -WindowStyle Hidden -PassThru
Write-Output "Started sensor collector PID $($process.Id) for Touch Bar PID $parentId; exits when that app ends."
