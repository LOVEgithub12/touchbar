[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$evidence=Join-Path $root 'verification-usbip-20261108'
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
Start-Transcript -Path (Join-Path $evidence 'install-usbipd.log') -Append | Out-Null
try {
    $principal=[Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
    if(-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Administrator required.'}
    $msi=Join-Path $root 'tools/downloads/usbipd-win_5.3.0_x64.msi'
    if((Get-FileHash -LiteralPath $msi).Hash -ne '1C984914AEC944DE19B64EFF232421439629699F8138E3DDC29301175BC6D938'){throw 'MSI hash mismatch.'}
    $signature=Get-AuthenticodeSignature -LiteralPath $msi
    if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notlike '*Frans van Dorsselaer*'){throw 'Official publisher signature invalid.'}
    if(Get-Service usbipd -ErrorAction SilentlyContinue){throw 'Existing usbipd installation found; inspect it instead of overwriting.'}
    Write-Output ('Installing verified usbipd 5.3.0; '+(Get-Date -Format o))
    $start=[Diagnostics.ProcessStartInfo]::new('C:\Windows\System32\msiexec.exe')
    $start.UseShellExecute=$false
    $start.CreateNoWindow=$true
    foreach($arg in @('/i',$msi,'/qn','/norestart','REBOOT=ReallySuppress','ADDLOCAL=Server,Drivers','/L*v',(Join-Path $evidence 'usbipd-msi.log'))){$start.ArgumentList.Add($arg)}
    $process=[Diagnostics.Process]::Start($start)
    $process.WaitForExit()
    Write-Output ('MsiExitCode='+$process.ExitCode)
    if($process.ExitCode -notin @(0,3010)){throw 'usbipd installation failed; inspect MSI log.'}
    # This task initially uses only a loopback client; do not expose the USB service to the LAN.
    $rules=@(Get-NetFirewallRule -DisplayName 'usbipd' -ErrorAction SilentlyContinue)
    if($rules.Count -ne 1){throw 'Expected exactly one installer firewall rule; inspect before binding any USB device.'}
    $rules | Disable-NetFirewallRule | Out-Null
    $rules | Get-NetFirewallAddressFilter | Select-Object LocalAddress,RemoteAddress | ConvertTo-Json | Write-Output
    Get-NetFirewallRule -DisplayName 'usbipd' | Select-Object DisplayName,Enabled,Direction,Action | ConvertTo-Json | Write-Output
    Get-Service usbipd,VBoxUSBMon | Select-Object Name,Status,StartType | ConvertTo-Json | Write-Output
    $exe='C:\Program Files\usbipd-win\usbipd.exe'
    & $exe --version
    & $exe list
} catch {
    Write-Output $_.Exception.ToString()
    exit 1
} finally {Stop-Transcript | Out-Null}
