[CmdletBinding()]
param([ValidateSet('Bind','Restore')][string]$Mode)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$evidence=Join-Path $root 'verification-usbip-20261108'
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
Start-Transcript -Path (Join-Path $evidence ('usbip-'+$Mode+'.log')) -Append | Out-Null
try {
    $principal=[Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
    if(-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Administrator required.'}
    $exe='C:\Program Files\usbipd-win\usbipd.exe'
    $id='USB\VID_05AC&PID_8302\0000000000000000'
    $key='HKLM:\SYSTEM\CurrentControlSet\Enum\'+$id
    $state=(& $exe state | Out-String) | ConvertFrom-Json
    $devices=@($state.Devices | Where-Object InstanceId -eq $id)
    if($devices.Count -ne 1){throw 'Expected one exact Touch Bar root record.'}
    $bus=$devices[0].BusId
    Write-Output ('Mode='+$Mode+';BusId='+$bus+';'+(Get-Date -Format o))
    if($Mode -eq 'Restore') {
        # Closing an imported connection briefly restarts the root, so its bus ID
        # may be absent while the persistent, identity-checked record still exists.
        if($devices[0].PersistedGuid){& $exe unbind --guid $devices[0].PersistedGuid}
        elseif($bus){& $exe unbind --busid $bus}
        else{throw 'Neither persistent identity nor connected bus ID is available.'}
        if($LASTEXITCODE -ne 0){throw 'USBIP unbind failed.'}
        for($attempt=0;$attempt -lt 40;$attempt++){
            $live=(& $exe state | Out-String)|ConvertFrom-Json
            $connected=@($live.Devices|Where-Object { $_.InstanceId -eq $id -and $_.BusId })
            if($connected.Count -eq 1){break}
            Start-Sleep -Milliseconds 250
        }
        & (Join-Path $PSScriptRoot 'BindTouchBar.ps1') -Mode Restore
        exit $LASTEXITCODE
    }
    if(-not $bus){throw 'Expected the exact Touch Bar root to be connected.'}
    $before=Get-ItemProperty -LiteralPath $key
    if($before.Service -ne 'HidUsb' -or $before.LowerFilters -notcontains 'AppleDFR'){throw 'Expected working Apple baseline.'}
    if((Get-NetFirewallRule -DisplayName usbipd).Enabled.ToString() -ne 'False'){throw 'LAN firewall rule must be disabled for the loopback test.'}
    $state | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidence 'before-usbip-state.json') -Encoding utf8
    Add-Type -Path (Join-Path $PSScriptRoot 'InboxDriverBinding.cs')
    try {
        [InboxDriverBinding]::SetLowerFilters($id,[string[]]@($before.LowerFilters | Where-Object {$_ -ne 'AppleDFR'}))
        & $exe bind --force --busid $bus
        if($LASTEXITCODE -ne 0){throw 'Force binding failed.'}
        $after=Get-ItemProperty -LiteralPath $key
        $after | Select-Object Service,Driver,ClassGUID,LowerFilters | Format-List | Out-String | Write-Output
        if($after.Service -ne 'VBoxUSB'){throw 'Expected VBoxUSB service after binding.'}
        & C:\Windows\System32\pnputil.exe /enum-devices /instanceid $id
        & $exe state
    } catch {
        Write-Output ('Binding failed: '+$_.Exception)
        & $exe unbind --busid $bus
        & (Join-Path $PSScriptRoot 'BindTouchBar.ps1') -Mode Restore
        throw
    }
} catch {
    Write-Output $_.Exception.ToString()
    exit 1
} finally {Stop-Transcript | Out-Null}
