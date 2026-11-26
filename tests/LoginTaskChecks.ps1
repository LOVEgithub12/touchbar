[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'TouchBar-USBIP/LoginTask.ps1')
function Require([bool]$ok,[string]$message){if(!$ok){throw $message}}
$sid='S-1-5-21-100-200-300-1001'
$scriptPath='C:\Test Folder\中文 & example\AutoStart.ps1'
$shell=Join-Path $env:WINDIR 'System32/WindowsPowerShell/v1.0/powershell.exe'
$text=Get-TouchBarLoginTaskXml -UserSid $sid -ScriptPath $scriptPath -PowerShellPath $shell
[xml]$xml=$text
Require (Test-TouchBarLoginTask -Xml $text -UserSid $sid -ScriptPath $scriptPath) 'Valid ordinary-user logon task rejected.'
Require ($xml.Task.Triggers.ChildNodes.Count -eq 1 -and $xml.Task.Triggers.LogonTrigger.Delay -eq 'PT0S') 'Unexpected trigger or logon delay.'
Require ($xml.Task.Principals.Principal.LogonType -eq 'InteractiveToken' -and $xml.Task.Principals.Principal.RunLevel -eq 'LeastPrivilege') 'Task escaped ordinary interactive session.'
Require ($xml.Task.Actions.Exec.Command -eq (Join-Path (Split-Path -Parent $scriptPath) 'SilentLogin.exe') -and [string]::IsNullOrWhiteSpace($xml.Task.Actions.Exec.Arguments)) 'Silent entry path or arguments were corrupted.'
Write-Output 'PASS: Immediate current-user logon task keeps ordinary desktop token and exact quoted path.'

foreach($mutation in @(
    @('LeastPrivilege','HighestAvailable'),@('InteractiveToken','ServiceAccount'),@('<Delay>PT0S</Delay>','<Delay>PT10S</Delay>'),
    @('<DisallowStartIfOnBatteries>false','<DisallowStartIfOnBatteries>true'),@('<RunOnlyIfIdle>false','<RunOnlyIfIdle>true'),
    @('<ExecutionTimeLimit>PT0S','<ExecutionTimeLimit>PT3M'),@('<MultipleInstancesPolicy>IgnoreNew','<MultipleInstancesPolicy>Parallel'),
    @('<Enabled>true','<Enabled>false'),@('<RunOnlyIfNetworkAvailable>false','<RunOnlyIfNetworkAvailable>true'),
    @('<AllowStartOnDemand>true','<AllowStartOnDemand>false'),@('<AllowHardTerminate>false','<AllowHardTerminate>true')
)) {
    Require (!(Test-TouchBarLoginTask -Xml $text.Replace($mutation[0],$mutation[1]) -UserSid $sid -ScriptPath $scriptPath)) ('Invalid task accepted: '+$mutation[1])
}
Require (!(Test-TouchBarLoginTask -Xml $text -UserSid 'S-1-5-21-100-200-300-1002' -ScriptPath $scriptPath)) 'Another user task accepted.'
Require (!(Test-TouchBarLoginTask -Xml $text -UserSid $sid -ScriptPath 'C:\Other\AutoStart.ps1')) 'Another program task accepted.'
Write-Output 'PASS: Verification rejects elevated, delayed, idle-only, battery-blocked, time-limited, duplicate, or foreign task definitions.'

$settings=$xml.Task.Settings
Require ($settings.AllowHardTerminate -eq 'false' -and $settings.StopIfGoingOnBatteries -eq 'false' -and $settings.RunOnlyIfNetworkAvailable -eq 'false' -and $settings.WakeToRun -eq 'false') 'Unwanted stop/network/wake policy.'
Require ((Get-TouchBarLoginTaskName -UserSid $sid) -ne (Get-TouchBarLoginTaskName -UserSid 'S-1-5-21-100-200-300-1002')) 'Task name not scoped by user.'
Write-Output 'PASS: Task stays available on battery and does not wake the PC or time-limit the application.'

$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
$currentSid=$identity.User.Value
$normalized=Get-TouchBarLoginTaskXml -UserSid $currentSid -ScriptPath $scriptPath -PowerShellPath $shell
foreach($field in @('<RunLevel>LeastPrivilege</RunLevel>','<Enabled>true</Enabled>','<Delay>PT0S</Delay>','<RunOnlyIfIdle>false</RunOnlyIfIdle>','<RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>','<AllowStartOnDemand>true</AllowStartOnDemand>','<WakeToRun>false</WakeToRun>')) {
    $normalized=$normalized.Replace($field,'')
}
$normalized=$normalized.Replace('<LogonTrigger><UserId>'+$currentSid+'</UserId>','<LogonTrigger><UserId>'+[Security.SecurityElement]::Escape($identity.Name)+'</UserId>')
Require (Test-TouchBarLoginTask -Xml $normalized -UserSid $currentSid -ScriptPath $scriptPath) 'Windows default omission/account normalization rejected.'
Require (!(Test-TouchBarLoginTask -Xml $normalized.Replace('<LogonTrigger>','<LogonTrigger><Delay>PT10S</Delay>') -UserSid $currentSid -ScriptPath $scriptPath)) 'Normalized task with actual delay accepted.'
Require (!(Test-TouchBarLoginTask -Xml $normalized.Replace('<LogonTrigger>','<LogonTrigger><Enabled>false</Enabled>') -UserSid $currentSid -ScriptPath $scriptPath)) 'Disabled normalized trigger accepted.'
Require (!(Test-TouchBarLoginTask -Xml $normalized.Replace('<UserId>'+[Security.SecurityElement]::Escape($identity.Name)+'</UserId>','') -UserSid $currentSid -ScriptPath $scriptPath)) 'All-user logon trigger accepted.'
Require (!(Test-TouchBarLoginTask -Xml $normalized.Replace('<ExecutionTimeLimit>PT0S</ExecutionTimeLimit>','') -UserSid $currentSid -ScriptPath $scriptPath)) 'Implicit 72-hour task limit accepted.'
Write-Output 'PASS: Saved Windows task defaults and account names resolve correctly; explicit unsafe changes remain rejected.'
$legacy=Get-TouchBarLoginTaskXml -UserSid $sid -ScriptPath $scriptPath -PowerShellPath $shell -LegacyPowerShell
Require (!(Test-TouchBarLoginTask -Xml $legacy -UserSid $sid -ScriptPath $scriptPath)) 'Direct console entry accepted as current task.'
Require (Test-TouchBarLoginTask -Xml $legacy -UserSid $sid -ScriptPath $scriptPath -AllowLegacyPowerShell) 'Exact old task rejected for migration.'
Require (!(Test-TouchBarLoginTask -Xml $legacy.Replace('LeastPrivilege','HighestAvailable') -UserSid $sid -ScriptPath $scriptPath -AllowLegacyPowerShell)) 'Legacy migration allowed elevated UI.'
Write-Output 'PASS: Legacy console entry is accepted only for migration; current task requires SilentLogin.exe.'
Write-Output '5/5 login task definition checks passed. No task, registry, device or process was changed.'
