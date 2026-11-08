[CmdletBinding()]
param([ValidateSet('Composite','WinUSB','Restore')][string]$Mode)
$ErrorActionPreference = 'Stop'
$workRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$logPath = Join-Path $workRoot ('verification-20261108/bind-' + $Mode + '.log')
Start-Transcript -Path $logPath -Append | Out-Null
try {
    $principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Administrator token required.' }
    Add-Type -Path (Join-Path $PSScriptRoot 'InboxDriverBinding.cs')
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class TouchBarPnPStatus {
 [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode)] static extern uint CM_Locate_DevNodeW(out uint node,string id,uint flags);
 [DllImport("cfgmgr32.dll")] static extern uint CM_Get_DevNode_Status(out uint status,out uint problem,uint node,uint flags);
 public static string Read(string id) {
  uint node,status,problem; uint result=CM_Locate_DevNodeW(out node,id,0);
  if(result!=0) return "Absent:CR="+result;
  result=CM_Get_DevNode_Status(out status,out problem,node,0);
  return "CR="+result+";Started="+((status&8)!=0)+";Problem="+problem+";Status=0x"+status.ToString("X");
 }
}
'@
    $root = [InboxDriverBinding]::ParentId
    $rootKey = 'HKLM:\SYSTEM\CurrentControlSet\Enum\' + $root
    $snapshot = Get-Content -LiteralPath (Join-Path $workRoot 'verification-20261108/before-device-properties.json') -Raw | ConvertFrom-Json
    $filterSnapshot = @($snapshot | Where-Object { $_.Path -eq $rootKey -and $_.Name -eq 'LowerFilters' })
    if ($filterSnapshot.Count -ne 1) { throw 'Original filter snapshot is missing or ambiguous.' }
    [string[]]$originalFilters = @($filterSnapshot[0].Value)
    $appleInf = 'C:\Windows\INF\oem171.inf'
    $usbInf = 'C:\Windows\INF\usb.inf'
    function Restore-AppleBinding {
        [InboxDriverBinding]::BindInherited($root,$appleInf,'AppleDFR_Device','USB\VID_05AC&PID_8302') | Format-List | Out-String | Write-Output
        [InboxDriverBinding]::SetLowerFilters($root,$originalFilters)
        & "$env:SystemRoot\System32\pnputil.exe" /restart-device $root
        Write-Output ([TouchBarPnPStatus]::Read($root))
        Get-ItemProperty -LiteralPath $rootKey | Select-Object Service,Driver,ClassGUID,LowerFilters | Format-List | Out-String | Write-Output
    }
    Write-Output ('Mode=' + $Mode + '; ' + (Get-Date -Format o))
    Write-Output ('Before: ' + [TouchBarPnPStatus]::Read($root))
    if ($Mode -eq 'Restore') { Restore-AppleBinding; exit 0 }
    if ($Mode -eq 'Composite') {
        $before = Get-ItemProperty -LiteralPath $rootKey
        if ($before.Service -ne 'HidUsb' -or $before.Driver -ne '{745a17a0-74d3-11d0-b6fe-00a0c90f57da}\0201') {
            throw 'Root binding changed since backup; inspect before retrying.'
        }
        if ((Get-AuthenticodeSignature -LiteralPath 'C:\Windows\System32\drivers\usbccgp.sys').Status -ne 'Valid') { throw 'USB composite signature invalid.' }
        $candidate = @([InboxDriverBinding]::ListInheritedCandidates($root,$usbInf) | Where-Object { $_.Section -eq 'Composite.Dev' -and $_.HardwareId -eq 'USB\COMPOSITE' })
        $restoreCandidate = @([InboxDriverBinding]::ListInheritedCandidates($root,$appleInf) | Where-Object { $_.Section -eq 'AppleDFR_Device' })
        if ($candidate.Count -ne 1 -or $restoreCandidate.Count -ne 1) { throw 'Exact install or restore model unavailable.' }
        try {
            [InboxDriverBinding]::SetLowerFilters($root,[string[]]@($originalFilters | Where-Object { $_ -ne 'AppleDFR' }))
            # This per-device configuration was already 1 in the preserved snapshot.
            if ((Get-ItemProperty -LiteralPath ($rootKey + '\Device Parameters')).OriginalConfigurationValue -ne 1) { throw 'Unexpected USB configuration; restore and inspect.' }
            $result = [InboxDriverBinding]::BindInherited($root,$usbInf,'Composite.Dev','USB\COMPOSITE')
            $result | Format-List | Out-String | Write-Output
            if ($result.NeedReboot) { throw 'Composite binding requires a full reboot; restoring original pending an interactive maintenance window.' }
            Write-Output ('After: ' + [TouchBarPnPStatus]::Read($root))
            if ((Get-ItemProperty -LiteralPath $rootKey).Service -ne 'usbccgp' -or [TouchBarPnPStatus]::Read($root) -notmatch 'Started=True;Problem=0') { throw 'Composite driver did not start cleanly.' }
        } catch {
            Write-Output ('INSTALL FAILED: ' + $_.Exception.ToString())
            Restore-AppleBinding
            throw
        }
    }
    if ($Mode -eq 'WinUSB') {
        if ((Get-ItemProperty -LiteralPath $rootKey).Service -ne 'usbccgp') { throw 'Composite parent is not active.' }
        $displayKey = 'HKLM:\SYSTEM\CurrentControlSet\Enum\USB\VID_05AC&PID_8302&MI_01'
        $connected = @(Get-ChildItem -LiteralPath $displayKey | ForEach-Object { 'USB\VID_05AC&PID_8302&MI_01\' + $_.PSChildName } | Where-Object { [TouchBarPnPStatus]::Read($_) -notlike 'Absent*' })
        if ($connected.Count -ne 1) { throw 'Expected exactly one connected Touch Bar display interface.' }
        $display = $connected[0]
        $displayParameters = 'HKLM:\SYSTEM\CurrentControlSet\Enum\' + $display + '\Device Parameters'
        $oldGuids = @((Get-ItemProperty -LiteralPath $displayParameters -ErrorAction SilentlyContinue).DeviceInterfaceGUIDs)
        $guids = [string[]]@(@($oldGuids + '{2003cacd-9e7c-477c-ab06-a5a8bbb1a63e}') | Where-Object { $_ } | Select-Object -Unique)
        if ((Get-AuthenticodeSignature -LiteralPath 'C:\Windows\System32\drivers\WinUSB.sys').Status -ne 'Valid') { throw 'WinUSB signature invalid.' }
        [InboxDriverBinding]::SetParameter($display,'DeviceInterfaceGUIDs',$guids,[Microsoft.Win32.RegistryValueKind]::MultiString)
        $result = [InboxDriverBinding]::Bind($display,'C:\Windows\INF\winusb.inf','WINUSB','USB\MS_COMP_WINUSB')
        $result | Format-List | Out-String | Write-Output
        if ($result.NeedReboot) { throw 'WinUSB requires a system reboot.' }
        Write-Output ([TouchBarPnPStatus]::Read($display))
        if ([TouchBarPnPStatus]::Read($display) -notmatch 'Started=True;Problem=0') { throw 'WinUSB display did not start cleanly.' }
    }
    & "$env:SystemRoot\System32\pnputil.exe" /enum-devices /instanceid $root
} catch {
    Write-Output ('ERROR: ' + $_.Exception.ToString())
    exit 1
} finally { Stop-Transcript | Out-Null }
