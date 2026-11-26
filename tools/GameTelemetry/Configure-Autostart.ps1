[CmdletBinding()]
param(
    [ValidateSet('Enable','Disable')][string]$Mode='Enable',
    [string]$RuntimeDirectory,
    [string]$TelemetryDirectory,
    [string]$UserSid
)
$ErrorActionPreference='Stop'
if(!$RuntimeDirectory){$RuntimeDirectory=Join-Path $PSScriptRoot '../../TouchBar-USBIP/app'}
if(!$TelemetryDirectory){$TelemetryDirectory=Join-Path $PSScriptRoot '../../WindowsTouchBar-master/WindowsTouchBar/Telemetry'}
$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
if(!$UserSid){$UserSid=$identity.User.Value}
. (Join-Path $PSScriptRoot 'SensorTask.ps1')
$taskName=Get-TouchBarSensorTaskName $UserSid
$RuntimeDirectory=[IO.Path]::GetFullPath($RuntimeDirectory)
$TelemetryDirectory=[IO.Path]::GetFullPath($TelemetryDirectory)
$programFiles=[Environment]::GetFolderPath('ProgramFiles')
$installRoot=Join-Path $programFiles 'TouchBarSensors'
$destination=Join-Path $installRoot $UserSid
$hostExe=Join-Path $destination 'TouchBarSensorHost.exe'
$isAdmin=([Security.Principal.WindowsPrincipal]::new($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if(!$isAdmin){
    function Quote([string]$value){"'"+$value.Replace("'","''")+"'"}
    $command='& '+(Quote $PSCommandPath)+' -Mode '+(Quote $Mode)+' -RuntimeDirectory '+(Quote $RuntimeDirectory)+' -TelemetryDirectory '+(Quote $TelemetryDirectory)+' -UserSid '+(Quote $UserSid)
    $encoded=[Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
    $shell=Join-Path $env:WINDIR 'System32/WindowsPowerShell/v1.0/powershell.exe'
    $installer=Start-Process -FilePath $shell -Verb RunAs -WindowStyle Hidden -ArgumentList @('-NoProfile','-NonInteractive','-EncodedCommand',$encoded) -PassThru
    $installer.WaitForExit()
    if($installer.ExitCode -ne 0){throw ('Sensor installation failed. See '+(Join-Path $destination 'install.log'))}
    Write-Output ('Sensor autostart '+$Mode+' completed. Task: '+$taskName)
    return
}
if($identity.User.Value -ne $UserSid){throw 'Approve installation using the same Windows account as the Touch Bar desktop session.'}
function Assert-NoReparse([string]$path) {
    $current=[IO.Path]::GetFullPath($path)
    while($current){
        if(Test-Path -LiteralPath $current){if((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw ('Reparse path refused: '+$current)}}
        $parent=Split-Path -Parent $current
        if($parent -eq $current){break};$current=$parent
    }
}
function Protect-Directory([string]$path) {
    Assert-NoReparse $path
    if(!(Test-Path -LiteralPath $path)){New-Item -ItemType Directory -Path $path | Out-Null}
    $acl=New-Object Security.AccessControl.DirectorySecurity
    $acl.SetAccessRuleProtection($true,$false)
    $admins=[Security.Principal.SecurityIdentifier]::new('S-1-5-32-544')
    $acl.SetOwner($admins)
    foreach($rule in @(@('S-1-5-32-544','FullControl'),@('S-1-5-18','FullControl'),@('S-1-5-32-545','ReadAndExecute'))){
        $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new($rule[0]),[Security.AccessControl.FileSystemRights]$rule[1],'ContainerInherit, ObjectInherit','None','Allow'))
    }
    Set-Acl -LiteralPath $path -AclObject $acl
}
function Stop-SensorHost {
    try {$event=[Threading.EventWaitHandle]::OpenExisting('Local\TouchBarSensorHost.Stop.'+$UserSid);try{[void]$event.Set()}finally{$event.Dispose()}}catch [Threading.WaitHandleCannotBeOpenedException]{}
    $deadline=[DateTime]::UtcNow.AddSeconds(15)
    do {
        $running=@(Get-Process -Name TouchBarSensorHost -ErrorAction SilentlyContinue | Where-Object {try{$_.Path -eq $hostExe}catch{$false}})
        if(!$running.Count){return};Start-Sleep -Milliseconds 250
    }while([DateTime]::UtcNow -lt $deadline)
    throw 'Existing sensor host did not stop; its files were not replaced.'
}
Assert-NoReparse $destination
Protect-Directory $installRoot
Protect-Directory $destination
$log=Join-Path $destination 'install.log'
# The protected directory is dedicated to this task. Refuse pre-existing links.
foreach($item in Get-ChildItem -LiteralPath $destination -Force){if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Unexpected link in the installation directory.'}}
$scheduler=New-Object -ComObject 'Schedule.Service'
$scheduler.Connect();$folder=$scheduler.GetFolder('\')
$existing=$null
try{$existing=$folder.GetTask($taskName)}catch{
    $failure=$_.Exception;while($failure.InnerException){$failure=$failure.InnerException}
    if($failure.HResult -notin @(-2147024894,-2147216625)){throw}
}
if($existing -and !(Test-TouchBarSensorTask -Xml $existing.Xml -UserSid $UserSid -ExecutablePath $hostExe)){throw 'Existing named task differs from the expected sensor task; it was not changed.'}
$oldXml=if($existing){$existing.Xml}else{$null}
$registered=$false
try {
    Add-Content -LiteralPath $log -Value ((Get-Date -Format o)+' '+$Mode+' requested for '+$UserSid) -Encoding UTF8
    if($Mode -eq 'Disable'){
        if($existing){$folder.DeleteTask($taskName,0)}
        Stop-SensorHost
        Add-Content -LiteralPath $log -Value 'Disabled. Protected files retained for re-enabling.' -Encoding UTF8
        return
    }
    $app=Join-Path $RuntimeDirectory 'WindowsTouchBar.exe'
    if(!(Test-Path -LiteralPath $app)){throw 'Touch Bar runtime missing.'}
    $names=@('TouchBarSensorHost.exe','TouchBarSensors.exe','LibreHardwareMonitorLib.dll','LibreHardwareMonitor-LICENSE.txt','provenance.json')
    $plan=@(foreach($name in $names){
        $source=Join-Path $TelemetryDirectory $name
        if(!(Test-Path -LiteralPath $source -PathType Leaf)){throw ('Missing install payload: '+$source)}
        [pscustomobject]@{Name=$name;Hash=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash}
    })
    Stop-SensorHost
    # Wait for children of the old host to release its protected executable/DLL.
    $deadline=[DateTime]::UtcNow.AddSeconds(12)
    do {
        $children=@(Get-Process -Name TouchBarSensors -ErrorAction SilentlyContinue | Where-Object {try{$_.Path -eq (Join-Path $destination 'TouchBarSensors.exe')}catch{$false}})
        if(!$children.Count){break};Start-Sleep -Milliseconds 250
    }while([DateTime]::UtcNow -lt $deadline)
    if($children.Count){throw 'Installed collector did not stop; no files were replaced.'}
    foreach($entry in $plan){
        $target=Join-Path $destination $entry.Name
        Copy-Item -LiteralPath (Join-Path $TelemetryDirectory $entry.Name) -Destination $target -Force
        if((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $entry.Hash){throw ('Copied file hash mismatch: '+$entry.Name)}
    }
    @{ApplicationPath=$app;UserSid=$UserSid} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $destination 'host.json') -Encoding UTF8
    $xml=Get-TouchBarSensorTaskXml -UserSid $UserSid -ExecutablePath $hostExe
    if(!(Test-TouchBarSensorTask -Xml $xml -UserSid $UserSid -ExecutablePath $hostExe)){throw 'Task definition failed validation.'}
    $sddl='D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GRGX;;;'+$UserSid+')'
    [void]$folder.RegisterTask($taskName,$xml,6,$UserSid,$null,3,$sddl);$registered=$true
    $saved=$folder.GetTask($taskName)
    if(!(Test-TouchBarSensorTask -Xml $saved.Xml -UserSid $UserSid -ExecutablePath $hostExe)){throw ('Saved task failed validation: '+$saved.Xml)}
    $saved.Xml | Set-Content -LiteralPath (Join-Path $destination 'task.xml') -Encoding Unicode
    [void]$saved.Run($null)
    @{InstalledAt=(Get-Date -Format o);TaskName=$taskName;UserSid=$UserSid;ApplicationPath=$app;Files=$plan} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $destination 'installation.json') -Encoding UTF8
    Add-Content -LiteralPath $log -Value 'Installed and started; windowless executable, interactive user, highest privileges.' -Encoding UTF8
}catch{
    $errorRecord=$_
    Add-Content -LiteralPath $log -Value $errorRecord.Exception.ToString() -Encoding UTF8
    if($registered){
        if($oldXml){[void]$folder.RegisterTask($taskName,$oldXml,6,$UserSid,$null,3,$null)}
        else{$folder.DeleteTask($taskName,0)}
        Stop-SensorHost
    }
    throw $errorRecord
}
