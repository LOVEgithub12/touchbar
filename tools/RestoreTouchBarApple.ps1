[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$app=Join-Path $root 'TouchBar-USBIP/app/WindowsTouchBar.exe'
# Cancel a login helper that may still be waiting for the device.
Set-Content -LiteralPath (Join-Path $root 'TouchBar-USBIP/autostart.disabled') -Value (Get-Date -Format o) -Encoding ASCII
if(Test-Path -LiteralPath $app){
    $stop=Start-Process -FilePath $app -ArgumentList '--stop' -WindowStyle Hidden -PassThru
    [void]$stop.WaitForExit(5000)
    foreach($process in @(Get-Process -Name WindowsTouchBar -ErrorAction SilentlyContinue)){
        if(-not $process.WaitForExit(10000)){throw 'Touch Bar program has not exited; restoration cancelled.'}
    }
}
$principal=[Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if(-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){
    $script=Join-Path $PSScriptRoot 'BindTouchBarUsbIp.ps1'
    $command="& '"+$script.Replace("'","''")+"' -Mode Restore"
    $encoded=[Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
    $elevated=Start-Process -FilePath (Join-Path $PSHOME 'pwsh.exe') -Verb RunAs -WindowStyle Hidden -ArgumentList @('-NoProfile','-NonInteractive','-EncodedCommand',$encoded) -PassThru
    $elevated.WaitForExit()
    exit $elevated.ExitCode
}
& (Join-Path $PSScriptRoot 'BindTouchBarUsbIp.ps1') -Mode Restore
exit $LASTEXITCODE
