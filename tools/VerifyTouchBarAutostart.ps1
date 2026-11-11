$ErrorActionPreference = 'Stop'
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Administrator access is required to inspect the protected SYSTEM recovery task. Access denied does not mean the task is missing.'
}
$workspace = Split-Path -Parent $PSScriptRoot
$task = Get-ScheduledTask -TaskName 'TouchBar USBIP Boot Recovery'
$info = $task | Get-ScheduledTaskInfo
if ($info.LastTaskResult -ne 0) { throw ('The boot recovery task last returned ' + $info.LastTaskResult) }
if ($task.Principal.UserId -ne 'SYSTEM' -and $task.Principal.UserId -ne 'S-1-5-18') { throw 'Unexpected task principal.' }
$record = [ordered]@{
    VerifiedAt = (Get-Date -Format o)
    TaskName = $task.TaskName
    State = [string]$task.State
    Principal = $task.Principal.UserId
    Trigger = @($task.Triggers | ForEach-Object { [ordered]@{Type=$_.CimClass.CimClassName;Enabled=$_.Enabled;Delay=$_.Delay} })
    Action = @($task.Actions | Select-Object Execute,Arguments,WorkingDirectory)
    LastRunTime = $info.LastRunTime.ToString('o')
    LastTaskResult = $info.LastTaskResult
    BatteryAllowed = -not $task.Settings.DisallowStartIfOnBatteries
    StartupCommand = (Get-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name WindowsTouchBar).WindowsTouchBar
    DeviceProblemCode = (Get-PnpDeviceProperty -InstanceId 'USB\VID_05AC&PID_8302\0000000000000000' -KeyName 'DEVPKEY_Device_ProblemCode').Data
}
$record | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $workspace 'verification-usbip-20261108\autostart-final.json') -Encoding UTF8
Export-ScheduledTask -TaskName $task.TaskName | Set-Content -LiteralPath (Join-Path $workspace 'verification-usbip-20261108\boot-recovery-task.xml') -Encoding UTF8
