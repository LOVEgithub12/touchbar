[CmdletBinding()]
param([ValidateSet('Media','Functions','Check')][string]$Mode='Media')
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$app=Join-Path $PSScriptRoot 'app/WindowsTouchBar.exe'
$log=Join-Path $PSScriptRoot 'launcher.log'
try {
    . (Join-Path $PSScriptRoot 'Read-UsbIpState.ps1')
    $exe='C:\Program Files\usbipd-win\usbipd.exe'
    if(-not (Test-Path -LiteralPath $exe)){throw 'usbipd is not installed. See the usage guide.'}
    if(-not (Test-Path -LiteralPath $app)){throw 'Touch Bar application is missing.'}
    $state=Read-UsbIpState -Executable $exe
    $devices=@($state.Devices | Where-Object InstanceId -eq 'USB\VID_05AC&PID_8302\0000000000000000')
    if($devices.Count -ne 1){throw 'The expected Touch Bar is not connected.'}
    $device=Get-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Enum\USB\VID_05AC&PID_8302\0000000000000000'
    if(-not $devices[0].IsForced -or $device.Service -ne 'VBoxUSB'){
        $script=Join-Path $root 'tools/BindTouchBarUsbIp.ps1'
        $command="& '"+$script.Replace("'","''")+"' -Mode Bind"
        $encoded=[Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
        $install=Start-Process -FilePath (Join-Path $PSHOME 'pwsh.exe') -Verb RunAs -WindowStyle Hidden -ArgumentList @('-NoProfile','-NonInteractive','-EncodedCommand',$encoded) -PassThru
        $install.WaitForExit()
        if($install.ExitCode -ne 0){throw 'Touch Bar driver binding failed. Inspect verification-usbip-20261108/usbip-Bind.log.'}
    }
    $modeArgument=switch($Mode){'Media'{'--media'}'Functions'{'--functions'}'Check'{'--check'}}
    if($Mode -ne 'Check'){
        $disabled=Join-Path $PSScriptRoot 'autostart.disabled'
        if(Test-Path -LiteralPath $disabled){Remove-Item -LiteralPath $disabled}
    }
    $launchTime=Get-Date
    $process=Start-Process -FilePath $app -ArgumentList @('--usbip',$modeArgument) -WorkingDirectory (Split-Path $app) -WindowStyle Hidden -PassThru
    if($Mode -eq 'Check'){
        if(-not $process.WaitForExit(60000)){throw 'Diagnostic timed out; inspect the application status log.'}
        $result=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'app/WindowsTouchBar.status.log') -Raw
        if($process.ExitCode -eq 2){throw 'The application is already running. Stop it before using Check.'}
        $result | Set-Content -LiteralPath $log -Encoding utf8
        if($process.ExitCode -ne 0){throw $result}
    }else{
        $statusPath=Join-Path $PSScriptRoot 'app/WindowsTouchBar.status.log'
        $deadline=[DateTime]::UtcNow.AddSeconds(25)
        $ready=$false
        $result=''
        while([DateTime]::UtcNow -lt $deadline){
            $process.Refresh()
            if((Test-Path -LiteralPath $statusPath) -and (Get-Item -LiteralPath $statusPath).LastWriteTime -ge $launchTime){
                $result=Get-Content -LiteralPath $statusPath -Raw
                if(-not $process.HasExited -and $result -match '(?m)^RUNNING:'){$ready=$true;break}
                if($result -match '(?m)^FAILED:'){throw $result}
            }
            if($process.HasExited){
                # The app deliberately exits 0 when a copy already owns the device.
                $existing=@(Get-Process -Name WindowsTouchBar -ErrorAction SilentlyContinue | Where-Object {
                    $_.Id -ne $process.Id -and $_.Path -eq $app -and $_.SessionId -eq (Get-Process -Id $PID).SessionId
                })
                if($process.ExitCode -eq 0 -and $existing.Count -gt 0){$ready=$true;break}
                throw ('Touch Bar initialization exited with code '+$process.ExitCode+'. '+$result)
            }
            Start-Sleep -Milliseconds 250
        }
        if(-not $ready){throw 'Touch Bar initialization timed out; inspect app/WindowsTouchBar.status.log.'}
        ('RUNNING '+$Mode+' at '+(Get-Date -Format o)) | Set-Content -LiteralPath $log -Encoding utf8
    }
}catch{
    $_.Exception.ToString() | Set-Content -LiteralPath $log -Encoding utf8
    Add-Type -TypeDefinition 'using System;using System.Runtime.InteropServices;public static class TouchBarLaunchMessage{[DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int MessageBox(IntPtr hwnd,string text,string caption,uint type);}'
    [void][TouchBarLaunchMessage]::MessageBox([IntPtr]::Zero,$_.Exception.Message + [Environment]::NewLine + 'Log: ' + $log,'Touch Bar',0x10)
    exit 1
}
