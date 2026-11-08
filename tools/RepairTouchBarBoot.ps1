[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
$evidence = Join-Path $workspace 'verification-usbip-20261108'
$log = Join-Path $evidence 'boot-driver-repair.log'
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Administrator required to restart the failed device.' }
Start-Transcript -Path $log -Append | Out-Null
try {
    $id = 'USB\VID_05AC&PID_8302\0000000000000000'
    $binding = Get-ItemProperty -LiteralPath ('HKLM:\SYSTEM\CurrentControlSet\Enum\' + $id)
    if ($binding.Service -ne 'VBoxUSB') { throw 'The Touch Bar is not bound to VBoxUSB; no device changed.' }
    if ((Get-Service -Name VBoxUSBMon).Status -ne 'Running') { Start-Service -Name VBoxUSBMon }
    if ((Get-Service -Name usbipd).Status -ne 'Running') { Start-Service -Name usbipd }
    & "$env:WINDIR\System32\pnputil.exe" /restart-device $id
    if ($LASTEXITCODE -ne 0) { throw ('Device restart failed: ' + $LASTEXITCODE) }
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        Start-Sleep -Milliseconds 500
        $problem = Get-PnpDeviceProperty -InstanceId $id -KeyName 'DEVPKEY_Device_ProblemCode'
        if ([uint32]$problem.Data -eq 0) { break }
    } while ([DateTime]::UtcNow -lt $deadline)
    Get-PnpDevice -InstanceId $id | Format-List Status,Class,FriendlyName,InstanceId
    $problem | Format-List KeyName,Data
    if ([uint32]$problem.Data -ne 0) { throw 'The Touch Bar still has a driver problem.' }
    'SUCCESS: precise device restart after VBoxUSBMon startup cleared the boot error.'
} catch {
    Write-Output $_.Exception.ToString()
    exit 1
} finally { Stop-Transcript | Out-Null }
