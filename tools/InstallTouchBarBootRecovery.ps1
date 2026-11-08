[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Administrator required to install the boot device recovery task.' }
$directory = Join-Path $env:ProgramFiles 'TouchBar-Startup'
$script = Join-Path $directory 'TouchBarBootRecovery.ps1'
$taskName = 'TouchBar USBIP Boot Recovery'
$existingTask = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
if ($existingTask -and $existingTask.State -eq 'Running') { throw 'The recovery task is running; retry after it finishes.' }
if (Test-Path -LiteralPath $directory) {
    if ((Get-Item -LiteralPath $directory).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Installation directory must not be a reparse point.' }
} else { New-Item -ItemType Directory -Path $directory | Out-Null }
# A privileged startup task must not execute user-writable workspace scripts.
$acl = New-Object Security.AccessControl.DirectorySecurity
$acl.SetSecurityDescriptorSddlForm('O:BAG:BAD:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;0x1200a9;;;BU)')
Set-Acl -LiteralPath $directory -AclObject $acl
$logPath = Join-Path $directory 'boot-recovery.log'
foreach ($target in @($script, $logPath)) {
    if ((Test-Path -LiteralPath $target) -and ((Get-Item -LiteralPath $target -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'Recovery script and log must not be reparse points.'
    }
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'TouchBarBootRecovery.ps1') -Destination $script -Force
$fileAcl = New-Object Security.AccessControl.FileSecurity
$fileAcl.SetSecurityDescriptorSddlForm('O:BAG:BAD:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;0x1200a9;;;BU)')
Set-Acl -LiteralPath $script -AclObject $fileAcl
if (-not (Test-Path -LiteralPath $logPath)) { New-Item -ItemType File -Path $logPath | Out-Null }
Set-Acl -LiteralPath $logPath -AclObject $fileAcl
$action = New-ScheduledTaskAction -Execute "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe" -Argument ('-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File "' + $script + '"') -WorkingDirectory $directory
$trigger = New-ScheduledTaskTrigger -AtStartup
$trigger.Delay = 'PT10S'
$taskPrincipal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Minutes 3) -MultipleInstances IgnoreNew -StartWhenAvailable
Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $taskPrincipal -Settings $settings -Description 'Only recover the exact Apple Touch Bar from VBoxUSB boot Code 37 after its monitor starts. Desktop UI starts separately as the signed-in user.' -Force | Out-Null
Start-ScheduledTask -TaskName $taskName
$record = [ordered]@{
    InstalledAt = (Get-Date -Format o)
    Task = $taskName
    Script = $script
    SourceSHA256 = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'TouchBarBootRecovery.ps1') -Algorithm SHA256).Hash
    InstalledSHA256 = (Get-FileHash -LiteralPath $script -Algorithm SHA256).Hash
    ProtectedDirectorySddl = (Get-Acl -LiteralPath $directory).Sddl
}
$record | ConvertTo-Json | Set-Content -LiteralPath (Join-Path (Split-Path $PSScriptRoot) 'verification-usbip-20261108\boot-recovery-install.json') -Encoding UTF8
