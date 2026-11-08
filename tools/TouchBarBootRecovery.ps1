# Installed as an administrator-owned copy under Program Files. This task never
# starts the desktop UI, changes a binding, or operates on any other USB device.
$ErrorActionPreference = 'Stop'
$id = 'USB\VID_05AC&PID_8302\0000000000000000'
$log = Join-Path $PSScriptRoot 'boot-recovery.log'
function Write-RecoveryLog([string]$message) {
    Add-Content -LiteralPath $log -Value ((Get-Date -Format o) + ' ' + $message) -Encoding UTF8
}
try {
    Set-Content -LiteralPath $log -Value ((Get-Date -Format o) + ' Boot device check started.') -Encoding UTF8
    $deadline = [DateTime]::UtcNow.AddSeconds(120)
    $lastReason = 'Waiting for the Touch Bar.'
    while ([DateTime]::UtcNow -lt $deadline) {
        $binding = Get-ItemProperty -LiteralPath ('HKLM:\SYSTEM\CurrentControlSet\Enum\' + $id) -ErrorAction SilentlyContinue
        if ($binding.Service -eq 'HidUsb') { Write-RecoveryLog 'Apple binding selected; no action.'; exit 0 }
        if ($binding.Service -eq 'VBoxUSB') {
            $problem = Get-PnpDeviceProperty -InstanceId $id -KeyName 'DEVPKEY_Device_ProblemCode' -ErrorAction SilentlyContinue
            $problemStatus = Get-PnpDeviceProperty -InstanceId $id -KeyName 'DEVPKEY_Device_ProblemStatus' -ErrorAction SilentlyContinue
            if ($null -ne $problem -and [uint32]$problem.Data -eq 0) {
                Write-RecoveryLog 'Touch Bar is healthy; no device restart needed.'
                exit 0
            }
            if ($null -ne $problem -and [uint32]$problem.Data -eq 37 -and
                $null -ne $problemStatus -and (([long]$problemStatus.Data) -band 4294967295L) -eq 3221225524L) {
                foreach ($name in @('VBoxUSBMon','usbipd')) {
                    if ((Get-Service -Name $name).Status -ne 'Running') { Start-Service -Name $name }
                }
                Write-RecoveryLog 'Confirmed Code 37 / STATUS_OBJECT_NAME_NOT_FOUND with VBoxUSBMon running; restarting the exact Touch Bar once.'
                & "$env:WINDIR\System32\pnputil.exe" /restart-device $id | Out-Null
                if ($LASTEXITCODE -ne 0) { throw ('Precise device restart failed: ' + $LASTEXITCODE) }
                for ($attempt = 0; $attempt -lt 30; $attempt++) {
                    Start-Sleep -Milliseconds 500
                    $after = Get-PnpDeviceProperty -InstanceId $id -KeyName 'DEVPKEY_Device_ProblemCode' -ErrorAction SilentlyContinue
                    if ($null -ne $after -and [uint32]$after.Data -eq 0) {
                        Write-RecoveryLog 'RECOVERED: Touch Bar driver problem code is 0.'
                        exit 0
                    }
                }
                throw 'Touch Bar did not recover after the single restart.'
            }
            $lastReason = 'Waiting for a settled device state; last problem code=' + $problem.Data
        }
        Start-Sleep -Seconds 2
    }
    throw ('Device wait expired. ' + $lastReason)
} catch {
    Write-RecoveryLog ('FAILED: ' + $_.Exception.Message)
    exit 1
}
