# Pure task definition, shared by installation and offline checks.
function Get-TouchBarSensorTaskName {
    param([Parameter(Mandatory=$true)][string]$UserSid)
    if ($UserSid -notmatch '^S-1-5-(?:\d+-)*\d+$') { throw 'Expected a Windows user SID.' }
    'WindowsTouchBar Sensors ' + $UserSid
}
function Get-TouchBarSensorTaskXml {
    param([Parameter(Mandatory=$true)][string]$UserSid,[Parameter(Mandatory=$true)][string]$ExecutablePath)
    [void](Get-TouchBarSensorTaskName $UserSid)
    $sid=[Security.SecurityElement]::Escape($UserSid)
    $exe=[Security.SecurityElement]::Escape([IO.Path]::GetFullPath($ExecutablePath))
    $dir=[Security.SecurityElement]::Escape((Split-Path -Parent ([IO.Path]::GetFullPath($ExecutablePath))))
    @"
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
 <RegistrationInfo><Description>Touch Bar hardware sensors: silently follow this user's Touch Bar process; the desktop UI remains unelevated.</Description></RegistrationInfo>
 <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>$sid</UserId></LogonTrigger></Triggers>
 <Principals><Principal id="SensorUser"><UserId>$sid</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
 <Settings>
  <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
  <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
  <AllowHardTerminate>false</AllowHardTerminate><StartWhenAvailable>true</StartWhenAvailable><RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
  <IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>
  <AllowStartOnDemand>true</AllowStartOnDemand><Enabled>true</Enabled><Hidden>false</Hidden><RunOnlyIfIdle>false</RunOnlyIfIdle>
  <WakeToRun>false</WakeToRun><ExecutionTimeLimit>PT0S</ExecutionTimeLimit><Priority>7</Priority>
  <RestartOnFailure><Interval>PT1M</Interval><Count>3</Count></RestartOnFailure>
 </Settings>
 <Actions Context="SensorUser"><Exec><Command>$exe</Command><WorkingDirectory>$dir</WorkingDirectory></Exec></Actions>
</Task>
"@
}
function Test-TouchBarSensorTask {
    param([Parameter(Mandatory=$true)][string]$Xml,[Parameter(Mandatory=$true)][string]$UserSid,[Parameter(Mandatory=$true)][string]$ExecutablePath)
    try {
        [xml]$doc=$Xml
        $ns=New-Object Xml.XmlNamespaceManager($doc.NameTable)
        $ns.AddNamespace('t','http://schemas.microsoft.com/windows/2004/02/mit/task')
        $ps=@($doc.SelectNodes('/t:Task/t:Principals/t:Principal',$ns))
        $ts=@($doc.SelectNodes('/t:Task/t:Triggers/*',$ns))
        $as=@($doc.SelectNodes('/t:Task/t:Actions/*',$ns))
        $settings=$doc.SelectSingleNode('/t:Task/t:Settings',$ns)
        if($ps.Count -ne 1 -or $ts.Count -ne 1 -or $as.Count -ne 1 -or !$settings){return $false}
        function Value($node,[string]$name,[string]$default) {
            $field=$node.SelectSingleNode(('t:'+$name),$ns)
            if($null -eq $field){return $default};$field.InnerText
        }
        function Sid([string]$account) {
            if($account -match '^S-1-'){return ([Security.Principal.SecurityIdentifier]::new($account)).Value}
            ([Security.Principal.NTAccount]::new($account)).Translate([Security.Principal.SecurityIdentifier]).Value
        }
        $p=$ps[0];$t=$ts[0];$a=$as[0]
        return (Sid $p.UserId) -eq $UserSid -and $p.LogonType -eq 'InteractiveToken' -and $p.RunLevel -eq 'HighestAvailable' -and
            $t.LocalName -eq 'LogonTrigger' -and (Sid $t.UserId) -eq $UserSid -and (Value $t 'Enabled' 'true') -eq 'true' -and
            [Xml.XmlConvert]::ToTimeSpan((Value $t 'Delay' 'PT0S')).Ticks -eq 0 -and
            $a.LocalName -eq 'Exec' -and [IO.Path]::GetFullPath($a.Command) -eq [IO.Path]::GetFullPath($ExecutablePath) -and
            [string]::IsNullOrWhiteSpace($a.Arguments) -and [IO.Path]::GetFullPath($a.WorkingDirectory) -eq (Split-Path -Parent ([IO.Path]::GetFullPath($ExecutablePath))) -and
            (Value $settings 'MultipleInstancesPolicy' 'IgnoreNew') -eq 'IgnoreNew' -and (Value $settings 'Enabled' 'true') -eq 'true' -and
            (Value $settings 'AllowStartOnDemand' 'true') -eq 'true' -and (Value $settings 'RunOnlyIfIdle' 'false') -eq 'false' -and
            (Value $settings 'RunOnlyIfNetworkAvailable' 'false') -eq 'false' -and (Value $settings 'WakeToRun' 'false') -eq 'false' -and
            $settings.DisallowStartIfOnBatteries -eq 'false' -and $settings.StopIfGoingOnBatteries -eq 'false' -and $settings.AllowHardTerminate -eq 'false' -and
            [Xml.XmlConvert]::ToTimeSpan((Value $settings 'ExecutionTimeLimit' 'PT72H')).Ticks -eq 0 -and
            $settings.RestartOnFailure.Count -eq '3' -and [Xml.XmlConvert]::ToTimeSpan($settings.RestartOnFailure.Interval).TotalSeconds -eq 60
    } catch { return $false }
}
