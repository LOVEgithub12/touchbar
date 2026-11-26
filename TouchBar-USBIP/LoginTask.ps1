# Pure definition shared by the installer and offline checks. This file does not
# contact Task Scheduler, change the registry, or start a process when loaded.
function Get-TouchBarLoginTaskName {
    param([Parameter(Mandatory=$true)][string]$UserSid)
    if ($UserSid -notmatch '^S-1-5-(?:\d+-)*\d+$') { throw 'A concrete Windows user SID is required.' }
    'WindowsTouchBar Login ' + $UserSid
}
function Get-TouchBarLoginTaskXml {
    param(
        [Parameter(Mandatory=$true)][string]$UserSid,
        [Parameter(Mandatory=$true)][string]$ScriptPath,
        [string]$PowerShellPath=(Join-Path $env:WINDIR 'System32/WindowsPowerShell/v1.0/powershell.exe'),
        [switch]$LegacyPowerShell
    )
    [void](Get-TouchBarLoginTaskName -UserSid $UserSid)
    $ScriptPath=[IO.Path]::GetFullPath($ScriptPath)
    $sid=[Security.SecurityElement]::Escape($UserSid)
    $executable=if($LegacyPowerShell){$PowerShellPath}else{Join-Path (Split-Path -Parent $ScriptPath) 'SilentLogin.exe'}
    $shell=[Security.SecurityElement]::Escape($executable)
    $arguments=if($LegacyPowerShell){[Security.SecurityElement]::Escape('-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File "'+$ScriptPath+'"')}else{''}
    $directory=[Security.SecurityElement]::Escape((Split-Path -Parent $ScriptPath))
    @"
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo><Description>Touch Bar: start immediately after this user logs on, using the ordinary interactive desktop token.</Description></RegistrationInfo>
  <Triggers><LogonTrigger><Enabled>true</Enabled><Delay>PT0S</Delay><UserId>$sid</UserId></LogonTrigger></Triggers>
  <Principals><Principal id="DesktopUser"><UserId>$sid</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>false</AllowHardTerminate><StartWhenAvailable>true</StartWhenAvailable><RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand><Enabled>true</Enabled><Hidden>false</Hidden><RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun><ExecutionTimeLimit>PT0S</ExecutionTimeLimit><Priority>6</Priority>
  </Settings>
  <Actions Context="DesktopUser"><Exec><Command>$shell</Command><Arguments>$arguments</Arguments><WorkingDirectory>$directory</WorkingDirectory></Exec></Actions>
</Task>
"@
}
function Test-TouchBarLoginTask {
    param([Parameter(Mandatory=$true)][string]$Xml,[Parameter(Mandatory=$true)][string]$UserSid,[Parameter(Mandatory=$true)][string]$ScriptPath,[switch]$AllowLegacyPowerShell)
    $ScriptPath=[IO.Path]::GetFullPath($ScriptPath)
    [xml]$task=$Xml
    $ns=New-Object Xml.XmlNamespaceManager($task.NameTable)
    $ns.AddNamespace('t','http://schemas.microsoft.com/windows/2004/02/mit/task')
    $principals=@($task.SelectNodes('/t:Task/t:Principals/t:Principal',$ns))
    $triggers=@($task.SelectNodes('/t:Task/t:Triggers/*',$ns))
    $actions=@($task.SelectNodes('/t:Task/t:Actions/*',$ns))
    $settings=$task.SelectSingleNode('/t:Task/t:Settings',$ns)
    if ($principals.Count -ne 1 -or $null -eq $settings -or $triggers.Count -ne 1 -or $actions.Count -ne 1) { return $false }
    $principal=$principals[0]
    # Windows omits default fields when exporting a saved task and may serialize
    # the logon trigger account as DOMAIN\name. Compare effective values and SIDs.
    function Read-TaskField($node,[string]$name,[string]$default) {
        $child=$node.SelectSingleNode(('t:'+$name),$ns)
        if($null -eq $child){return $default}
        $child.InnerText
    }
    function Resolve-TaskUserSid([string]$account) {
        if([string]::IsNullOrWhiteSpace($account)){return ''}
        try {
            if($account -match '^S-1-') {return ([Security.Principal.SecurityIdentifier]::new($account)).Value}
            ([Security.Principal.NTAccount]::new($account)).Translate([Security.Principal.SecurityIdentifier]).Value
        } catch {return ''}
    }
    try {
        $noDelay=[Xml.XmlConvert]::ToTimeSpan((Read-TaskField $triggers[0] 'Delay' 'PT0S')).Ticks -eq 0
        $noLimit=[Xml.XmlConvert]::ToTimeSpan((Read-TaskField $settings 'ExecutionTimeLimit' 'PT72H')).Ticks -eq 0
    } catch {return $false}
    $expectedShell=Join-Path (Split-Path -Parent $ScriptPath) 'SilentLogin.exe'
    $validAction=$actions[0].LocalName -eq 'Exec' -and [IO.Path]::GetFullPath($actions[0].Command) -eq [IO.Path]::GetFullPath($expectedShell) -and [string]::IsNullOrWhiteSpace($actions[0].Arguments)
    if($AllowLegacyPowerShell){
        $legacyShell=Join-Path $env:WINDIR 'System32/WindowsPowerShell/v1.0/powershell.exe'
        $legacyArgs='-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File "'+$ScriptPath+'"'
        $validAction=$validAction -or ($actions[0].LocalName -eq 'Exec' -and [IO.Path]::GetFullPath($actions[0].Command) -eq [IO.Path]::GetFullPath($legacyShell) -and $actions[0].Arguments -eq $legacyArgs)
    }
    return (Resolve-TaskUserSid $principal.UserId) -eq $UserSid -and $principal.LogonType -eq 'InteractiveToken' -and (Read-TaskField $principal 'RunLevel' 'LeastPrivilege') -eq 'LeastPrivilege' -and
        $triggers[0].LocalName -eq 'LogonTrigger' -and (Resolve-TaskUserSid $triggers[0].UserId) -eq $UserSid -and (Read-TaskField $triggers[0] 'Enabled' 'true') -eq 'true' -and $noDelay -and
        $validAction -and
        [IO.Path]::GetFullPath($actions[0].WorkingDirectory) -eq (Split-Path -Parent $ScriptPath) -and
        (Read-TaskField $settings 'Enabled' 'true') -eq 'true' -and (Read-TaskField $settings 'MultipleInstancesPolicy' 'IgnoreNew') -eq 'IgnoreNew' -and
        (Read-TaskField $settings 'RunOnlyIfIdle' 'false') -eq 'false' -and $settings.DisallowStartIfOnBatteries -eq 'false' -and $settings.StopIfGoingOnBatteries -eq 'false' -and $noLimit -and
        (Read-TaskField $settings 'RunOnlyIfNetworkAvailable' 'false') -eq 'false' -and (Read-TaskField $settings 'WakeToRun' 'false') -eq 'false' -and
        (Read-TaskField $settings 'AllowStartOnDemand' 'true') -eq 'true' -and $settings.AllowHardTerminate -eq 'false'
}
