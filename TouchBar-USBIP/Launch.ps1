[CmdletBinding()]
param([ValidateSet('Media','Functions','Check')][string]$Mode='Media')
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$app=Join-Path $PSScriptRoot 'app/WindowsTouchBar.exe'
$log=Join-Path $PSScriptRoot 'launcher.log'
try {
    $exe='C:\Program Files\usbipd-win\usbipd.exe'
    if(-not (Test-Path -LiteralPath $exe)){throw 'usbipd is not installed. See the usage guide.'}
    if(-not (Test-Path -LiteralPath $app)){throw 'Touch Bar application is missing.'}
    $state=(& $exe state | Out-String) | ConvertFrom-Json
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
    $process=Start-Process -FilePath $app -ArgumentList @('--usbip',$modeArgument) -WindowStyle Hidden -PassThru
    if($Mode -eq 'Check'){
        if(-not $process.WaitForExit(60000)){throw 'Diagnostic timed out; inspect the application status log.'}
        $result=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'app/WindowsTouchBar.status.log') -Raw
        if($process.ExitCode -eq 2){throw 'The application is already running. Stop it before using Check.'}
        $result | Set-Content -LiteralPath $log -Encoding utf8
        if($process.ExitCode -ne 0){throw $result}
    }else{
        ('Started '+$Mode+' at '+(Get-Date -Format o)) | Set-Content -LiteralPath $log -Encoding utf8
    }
}catch{
    $_.Exception.ToString() | Set-Content -LiteralPath $log -Encoding utf8
    Add-Type -TypeDefinition 'using System;using System.Runtime.InteropServices;public static class TouchBarLaunchMessage{[DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int MessageBox(IntPtr hwnd,string text,string caption,uint type);}'
    [void][TouchBarLaunchMessage]::MessageBox([IntPtr]::Zero,$_.Exception.Message + [Environment]::NewLine + 'Log: ' + $log,'Touch Bar',0x10)
    exit 1
}
