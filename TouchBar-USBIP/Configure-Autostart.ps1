[CmdletBinding()]
param([ValidateSet('Enable','Disable')][string]$Mode = 'Enable')
$ErrorActionPreference = 'Stop'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$valueName = 'WindowsTouchBar'
$disabled = Join-Path $PSScriptRoot 'autostart.disabled'
$script = Join-Path $PSScriptRoot 'AutoStart.ps1'
$shell = "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe"
. (Join-Path $PSScriptRoot 'LoginTask.ps1')
$sid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$taskName=Get-TouchBarLoginTaskName -UserSid $sid
$scheduler=New-Object -ComObject 'Schedule.Service'
$scheduler.Connect()
$folder=$scheduler.GetFolder('\')
$existing=$null
try { $existing=$folder.GetTask($taskName) }
catch {
    $errorValue=$_.Exception
    while($null -ne $errorValue.InnerException){$errorValue=$errorValue.InnerException}
    if ($errorValue.HResult -notin @(-2147024894,-2147216625)) {throw}
}
$oldXml=if($null -ne $existing){$existing.Xml}else{$null}
if($oldXml -and !(Test-TouchBarLoginTask -Xml $oldXml -UserSid $sid -ScriptPath $script -AllowLegacyPowerShell)) {
    throw 'The named task has a different action or security context; it was not changed.'
}
$key=Get-ItemProperty -LiteralPath $runKey -ErrorAction SilentlyContinue
$oldRun=if($null -ne $key){$key.$valueName}else{$null}
if($oldRun -and $oldRun.IndexOf(('"'+$script+'"'),[StringComparison]::OrdinalIgnoreCase) -lt 0) {
    throw 'The existing WindowsTouchBar Run entry points elsewhere; it was not changed.'
}
$oldDisabled=if(Test-Path -LiteralPath $disabled){[IO.File]::ReadAllText($disabled)}else{$null}
$backup=Join-Path $PSScriptRoot ('autostart-registration-'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff')+'.json')
@{Time=(Get-Date -Format o);UserSid=$sid;TaskName=$taskName;TaskXml=$oldXml;RunValue=$oldRun;DisabledContent=$oldDisabled;RequestedMode=$Mode} |
    ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $backup -Encoding UTF8
if ($Mode -eq 'Disable') {
    Set-Content -LiteralPath $disabled -Value (Get-Date -Format o) -Encoding ASCII
    if($null -ne $existing){$folder.DeleteTask($taskName,0)}
    if($null -ne $oldRun){Remove-ItemProperty -LiteralPath $runKey -Name $valueName}
    Write-Output ('Touch Bar automatic login startup disabled. Backup: '+$backup)
    return
}
if (!(Test-Path -LiteralPath $script)) {throw 'AutoStart.ps1 is missing.'}
& (Join-Path $PSScriptRoot 'Build-SilentLogin.ps1')
$xml=Get-TouchBarLoginTaskXml -UserSid $sid -ScriptPath $script -PowerShellPath $shell
$registered=$false
try {
    # TASK_CREATE_OR_UPDATE=6; TASK_LOGON_INTERACTIVE_TOKEN=3. No stored password,
    # SYSTEM account, boot trigger, or elevated desktop application.
    [void]$folder.RegisterTask($taskName,$xml,6,$sid,$null,3,$null)
    $registered=$true
    $saved=$folder.GetTask($taskName)
    if (!(Test-TouchBarLoginTask -Xml $saved.Xml -UserSid $sid -ScriptPath $script)) {throw ('Registered task failed verification. Saved definition: '+$saved.Xml)}
    # Remove the old trigger only after the new task has been saved and verified.
    if($null -ne $oldRun){Remove-ItemProperty -LiteralPath $runKey -Name $valueName}
    if(Test-Path -LiteralPath $disabled){Remove-Item -LiteralPath $disabled}
    Write-Output ('Touch Bar immediate login task enabled: '+$taskName+'. Backup: '+$backup)
} catch {
    $failure=$_
    if($registered) {
        if($oldXml){[void]$folder.RegisterTask($taskName,$oldXml,6,$sid,$null,3,$null)}
        else{$folder.DeleteTask($taskName,0)}
    }
    if($null -ne $oldRun){New-ItemProperty -LiteralPath $runKey -Name $valueName -Value $oldRun -PropertyType String -Force | Out-Null}
    if($null -ne $oldDisabled){[IO.File]::WriteAllText($disabled,$oldDisabled)}
    throw $failure
}
