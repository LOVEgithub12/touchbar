[CmdletBinding()]
param([string]$ToolsDirectory,[string]$TelemetryDirectory)
$ErrorActionPreference='Stop'
if(!$ToolsDirectory){$ToolsDirectory=Join-Path $PSScriptRoot '../tools/GameTelemetry'}
if(!$TelemetryDirectory){$TelemetryDirectory=Join-Path $PSScriptRoot '../WindowsTouchBar-master/WindowsTouchBar/Telemetry'}
. (Join-Path $ToolsDirectory 'SensorTask.ps1')
$sid='S-1-5-21-111-222-333-1001'
$exe='C:\Program Files\TouchBarSensors\test\TouchBarSensorHost.exe'
$xml=Get-TouchBarSensorTaskXml -UserSid $sid -ExecutablePath $exe
$passed=0
function Assert([bool]$condition,[string]$message){if(!$condition){throw $message};$script:passed++;Write-Output ('PASS '+$message)}
Assert (Test-TouchBarSensorTask $xml $sid $exe) 'Expected elevated interactive-user task accepted'
Assert (!(Test-TouchBarSensorTask ($xml.Replace('HighestAvailable','LeastPrivilege')) $sid $exe)) 'Unelevated sensor task rejected'
Assert (!(Test-TouchBarSensorTask ($xml.Replace('InteractiveToken','ServiceAccount')) $sid $exe)) 'Service-account execution rejected'
Assert (!(Test-TouchBarSensorTask ($xml.Replace('<Command>','<Arguments>unexpected</Arguments><Command>')) $sid $exe)) 'Extra command arguments rejected'
Assert (!(Test-TouchBarSensorTask ($xml.Replace('<LogonTrigger>','<LogonTrigger><Delay>PT30S</Delay>')) $sid $exe)) 'Delayed startup rejected'
Assert (!(Test-TouchBarSensorTask ($xml.Replace('PT0S</ExecutionTimeLimit>','PT72H</ExecutionTimeLimit>')) $sid $exe)) 'Automatic time limit rejected'
Assert (!(Test-TouchBarSensorTask ($xml.Replace('<DisallowStartIfOnBatteries>false','<DisallowStartIfOnBatteries>true')) $sid $exe)) 'Battery startup restriction rejected'
Assert (!(Test-TouchBarSensorTask ($xml.Replace($sid,'S-1-5-21-111-222-333-1002')) $sid $exe)) 'Different user rejected'
Assert (!(Test-TouchBarSensorTask $xml $sid 'C:\Other\TouchBarSensorHost.exe')) 'Different executable rejected'
Assert (!(Test-TouchBarSensorTask ($xml.Replace('IgnoreNew','Parallel')) $sid $exe)) 'Parallel instances rejected'
$hostPath=Join-Path $TelemetryDirectory 'TouchBarSensorHost.exe'
$stream=[IO.File]::OpenRead($hostPath)
$reader=[IO.BinaryReader]::new($stream)
try {
    $stream.Position=0x3c;$offset=$reader.ReadInt32()
    $stream.Position=$offset+24+68
    Assert ($reader.ReadUInt16() -eq 2) 'Host PE subsystem is Windows GUI (no console)'
}finally{$reader.Dispose()}
$assembly=[Reflection.Assembly]::LoadFile([IO.Path]::GetFullPath($hostPath))
$matcher=$assembly.GetType('SensorAutoStartHost').GetMethod('MatchesCollector',[Reflection.BindingFlags]'Static,NonPublic')
$installed='C:\Program Files\TouchBarSensors\test\TouchBarSensors.exe';$legacy='C:\runtime\Telemetry\TouchBarSensors.exe'
Assert ([bool]$matcher.Invoke($null,@($installed,'collector --serve --parent 42 --owner 2',$installed,$legacy,42))) 'Adopt matching installed collector'
Assert ([bool]$matcher.Invoke($null,@($legacy,'collector --serve --parent 42',$installed,$legacy,42))) 'Adopt existing manual session collector'
Assert (!([bool]$matcher.Invoke($null,@($legacy,'collector --serve --parent 420',$installed,$legacy,42)))) 'Do not confuse PID prefix with parent PID'
Assert (!([bool]$matcher.Invoke($null,@('C:\other\TouchBarSensors.exe','collector --serve --parent 42',$installed,$legacy,42)))) 'Do not adopt another executable'
Assert (!([bool]$matcher.Invoke($null,@($legacy,'collector --parent 42',$installed,$legacy,42)))) 'Do not adopt ordinary stdout collector'
$configType=$assembly.GetType('SensorAutoStartHost+Configuration')
Add-Type -AssemblyName System.Web.Extensions
$serializer=New-Object Web.Script.Serialization.JavaScriptSerializer
$config=$serializer.Deserialize('{"ApplicationPath":"C:\\runtime\\WindowsTouchBar.exe","UserSid":"S-1-5-21-111-222-333-1001"}',$configType)
Assert ($config.ApplicationPath -eq 'C:\runtime\WindowsTouchBar.exe' -and $config.UserSid -eq $sid) 'Installed configuration deserializes correctly'
Write-Output ($passed.ToString()+'/17 sensor autostart checks passed.')
