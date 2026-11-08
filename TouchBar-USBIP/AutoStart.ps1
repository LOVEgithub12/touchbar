[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$app = Join-Path $PSScriptRoot 'app\WindowsTouchBar.exe'
$statusPath = Join-Path $PSScriptRoot 'app\WindowsTouchBar.status.log'
$logPath = Join-Path $PSScriptRoot 'autostart.log'
$disabledPath = Join-Path $PSScriptRoot 'autostart.disabled'
$deviceId = 'USB\VID_05AC&PID_8302\0000000000000000'
$usbipd = 'C:\Program Files\usbipd-win\usbipd.exe'
$mutex = New-Object System.Threading.Mutex($false, 'Local\WindowsTouchBar.AutoStart')
$ownsMutex = $false
function Write-StartupLog([string]$message) {
    Add-Content -LiteralPath $logPath -Value ((Get-Date -Format o) + ' ' + $message) -Encoding UTF8
}
function Find-RunningApp {
    @(Get-Process -Name WindowsTouchBar -ErrorAction SilentlyContinue | Where-Object {
        try { $_.Path -eq $app -and $_.SessionId -eq (Get-Process -Id $PID).SessionId } catch { $false }
    })
}
try {
    try { $ownsMutex = $mutex.WaitOne(0) } catch [System.Threading.AbandonedMutexException] { $ownsMutex = $true }
    if (-not $ownsMutex -or (Test-Path -LiteralPath $disabledPath)) { exit 0 }
    Set-Content -LiteralPath $logPath -Value ((Get-Date -Format o) + ' Login startup requested.') -Encoding UTF8
    if ((Find-RunningApp).Count -gt 0) { Write-StartupLog 'Application is already running.'; exit 0 }
    if (-not (Test-Path -LiteralPath $app) -or -not (Test-Path -LiteralPath $usbipd)) {
        throw 'Touch Bar application or usbipd is missing.'
    }
    $deadline = [DateTime]::UtcNow.AddSeconds(150)
    $attempt = 0
    $lastReason = 'Waiting for the USBIP service and Touch Bar.'
    while ([DateTime]::UtcNow -lt $deadline) {
        if (Test-Path -LiteralPath $disabledPath) { Write-StartupLog 'Automatic startup was disabled.'; exit 0 }
        if ((Find-RunningApp).Count -gt 0) { Write-StartupLog 'Application was started separately.'; exit 0 }
        try {
            if ((Get-Service -Name usbipd).Status -ne 'Running') { throw 'USBIP service is not running yet.' }
            $device = Get-ItemProperty -LiteralPath ('HKLM:\SYSTEM\CurrentControlSet\Enum\' + $deviceId)
            if ($device.Service -ne 'VBoxUSB') {
                if ($device.Service -eq 'HidUsb') {
                    Write-StartupLog 'Apple/original binding is selected; automatic startup does not change drivers.'
                    exit 0
                }
                throw 'The Touch Bar driver is still being enumerated.'
            }
            $state = (& $usbipd state | Out-String) | ConvertFrom-Json
            if ($LASTEXITCODE -ne 0) { throw 'USBIP device enumeration failed.' }
            $matches = @($state.Devices | Where-Object { $_.InstanceId -eq $deviceId -and $_.BusId -and $_.IsForced })
            if ($matches.Count -ne 1) { throw 'The exact shared Touch Bar is not ready.' }
            $problem = Get-PnpDeviceProperty -InstanceId $deviceId -KeyName 'DEVPKEY_Device_ProblemCode'
            if ([uint32]$problem.Data -ne 0) { throw ('Touch Bar driver problem code: ' + $problem.Data) }
            $attempt++
            Write-StartupLog ('Starting application; attempt ' + $attempt + '.')
            $launchTime = Get-Date
            $process = Start-Process -FilePath $app -ArgumentList '--usbip','--background' -WorkingDirectory (Split-Path $app) -WindowStyle Hidden -PassThru
            $startDeadline = [DateTime]::UtcNow.AddSeconds(25)
            while (-not $process.HasExited -and [DateTime]::UtcNow -lt $startDeadline) {
                if ((Test-Path -LiteralPath $statusPath) -and (Get-Item -LiteralPath $statusPath).LastWriteTime -ge $launchTime) {
                    $status = Get-Content -LiteralPath $statusPath -Raw
                    if ($status -match '(?m)^RUNNING:') {
                        Write-StartupLog ('RUNNING; process ' + $process.Id + '. Startup helper exiting.')
                        exit 0
                    }
                }
                Start-Sleep -Milliseconds 500
                $process.Refresh()
            }
            if (-not $process.HasExited) {
                throw 'Application startup is still pending; see its status log.'
            }
            if ($process.ExitCode -eq 0) {
                Write-StartupLog 'Application stopped normally; no automatic restart.'
                exit 0
            }
            $lastReason = 'Application startup failed with exit code ' + $process.ExitCode + '.'
            Write-StartupLog $lastReason
            if ($attempt -ge 3) { throw 'Three application startup attempts failed.' }
        } catch {
            $lastReason = $_.Exception.Message
            if ($attempt -ge 3 -or (Find-RunningApp).Count -gt 0) { throw }
        }
        Start-Sleep -Seconds 3
    }
    throw ('Startup wait expired. ' + $lastReason)
} catch {
    Write-StartupLog ('FAILED: ' + $_.Exception.Message)
    exit 1
} finally {
    if ($ownsMutex) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
