[CmdletBinding()]
param([string]$TelemetryDirectory = (Join-Path $PSScriptRoot '../../WindowsTouchBar-master/WindowsTouchBar/Telemetry'))
$ErrorActionPreference = 'Stop'
$TelemetryDirectory = [IO.Path]::GetFullPath($TelemetryDirectory)
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.x C# compiler is required for the sensor helpers.' }
New-Item -ItemType Directory -Path $TelemetryDirectory -Force | Out-Null

# The host has no hardware-library dependency and is checked on clean source builds.
$hostOutput = Join-Path $TelemetryDirectory 'TouchBarSensorHost.exe'
& $compiler /nologo /target:winexe "/out:$hostOutput" /r:System.Management.dll /r:System.Web.Extensions.dll (Join-Path $PSScriptRoot 'SensorAutoStartHost.cs')
if ($LASTEXITCODE -ne 0) { throw 'Sensor autostart host build failed.' }

$library = Join-Path $TelemetryDirectory 'LibreHardwareMonitorLib.dll'
if (!(Test-Path -LiteralPath $library)) {
    Write-Output 'Hardware library absent; optional sensor collector not built. Sensor host built for offline checks.'
    return
}
$output = Join-Path $TelemetryDirectory 'TouchBarSensors.exe'
& $compiler /nologo /target:exe "/out:$output" "/r:$library" /r:System.Web.Extensions.dll (Join-Path $PSScriptRoot 'TouchBarSensors.cs')
if ($LASTEXITCODE -ne 0) { throw 'Sensor collector build failed.' }
