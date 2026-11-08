[CmdletBinding()]
param(
    [ValidateSet('Status', 'Plan')]
    [string]$Mode = 'Status',
    [switch]$Plan
)

# Read-only status and plan. Native selection was verified on this machine on
# 2026-11-08; device changes belong to the separate administrator BindTouchBar.ps1.
$ErrorActionPreference = 'Stop'
$touchBarParentId = 'USB\VID_05AC&PID_8302\0000000000000000'
$touchBarParentKey = 'HKLM:\SYSTEM\CurrentControlSet\Enum\' + $touchBarParentId
$touchBarInterfaceGuid = '{2003cacd-9e7c-477c-ab06-a5a8bbb1a63e}'

function Get-TouchBarDeviceRecord {
    param([string]$InstanceId)
    $deviceKey = 'HKLM:\SYSTEM\CurrentControlSet\Enum\' + $InstanceId
    if (-not (Test-Path -LiteralPath $deviceKey)) {
        return [pscustomobject]@{ InstanceId = $InstanceId; Recorded = $false }
    }
    $device = Get-ItemProperty -LiteralPath $deviceKey
    $parameters = Get-ItemProperty -LiteralPath ($deviceKey + '\Device Parameters') -ErrorAction SilentlyContinue
    $classDriver = $null
    if ($device.Driver) {
        $classDriver = Get-ItemProperty -LiteralPath ('HKLM:\SYSTEM\CurrentControlSet\Control\Class\' + $device.Driver) -ErrorAction SilentlyContinue
    }
    $pnpStatus = & "$env:SystemRoot\System32\pnputil.exe" /enum-devices /instanceid $InstanceId 2>&1
    [pscustomobject]@{
        InstanceId = $InstanceId
        Recorded = $true
        DeviceDescription = $device.DeviceDesc
        Service = $device.Service
        LowerFilters = @($device.LowerFilters)
        DriverInf = $classDriver.InfPath
        DriverSection = $classDriver.InfSection
        DriverPackagePresent = if ($classDriver.InfPath) { Test-Path -LiteralPath (Join-Path "$env:SystemRoot\INF" $classDriver.InfPath) } else { $false }
        OriginalConfigurationValue = $parameters.OriginalConfigurationValue
        InterfaceGuids = @($parameters.DeviceInterfaceGUIDs)
        PnpUtilStatus = ($pnpStatus -join [Environment]::NewLine)
    }
}

$deviceRecords = @(Get-TouchBarDeviceRecord -InstanceId $touchBarParentId)
foreach ($interface in @('00', '01')) {
    $interfaceKey = 'HKLM:\SYSTEM\CurrentControlSet\Enum\USB\VID_05AC&PID_8302&MI_' + $interface
    if (Test-Path -LiteralPath $interfaceKey) {
        foreach ($instance in Get-ChildItem -LiteralPath $interfaceKey) {
            $deviceRecords += Get-TouchBarDeviceRecord -InstanceId ('USB\VID_05AC&PID_8302&MI_' + $interface + '\' + $instance.PSChildName)
        }
    }
}

$result = [ordered]@{
    ReadOnly = $true
    ParentInstanceId = $touchBarParentId
    Devices = $deviceRecords
}

if ($Plan -or $Mode -eq 'Plan') {
    $inboxFiles = foreach ($fileName in @('usbccgp.sys', 'WinUSB.sys')) {
        $filePath = Join-Path "$env:SystemRoot\System32\drivers" $fileName
        $signature = Get-AuthenticodeSignature -LiteralPath $filePath
        [pscustomobject]@{
            Path = $filePath
            SignatureStatus = [string]$signature.Status
            Signer = $signature.SignerCertificate.Subject
        }
    }
    $result.InboxBinaries = @($inboxFiles)
    $result.VerifiedModels = @(
        [pscustomobject]@{ Device = 'Parent'; Inf = (Join-Path "$env:SystemRoot\INF" 'usb.inf'); InstallSection = 'Composite.Dev'; Service = 'usbccgp'; MatchingId = 'USB\COMPOSITE' },
        [pscustomobject]@{ Device = 'MI_01 only'; Inf = (Join-Path "$env:SystemRoot\INF" 'winusb.inf'); InstallSection = 'WINUSB'; Service = 'WINUSB'; MatchingId = 'USB\MS_COMP_WINUSB' }
    )
    $result.InterfaceGuid = $touchBarInterfaceGuid
    $result.VerificationDate = '2026-11-08'
    $result.NativeSelectionVerified = $true
    $result.DriverBindingVerified = $true
    $result.PhysicalTouchVerified = $true
    $result.DisplayHandshakeVerified = $true
    $result.CustomModeReady = $true
    $result.WorkingTransport = 'Local USB/IP via signed usbipd-win 5.3.0 / VBoxUSB; not WinUSB'
    $result.WinUsbDisplayVerified = $false
    $result.AppleRestoreVerified = $true
    $result.LastVerifiedBinding = 'VBoxUSB / oem194.inf on the exact Touch Bar root, using usbipd-win 5.3.0. User confirmed 2008x60 pattern and media UI display plus working touch volume controls. Apple restore and subsequent custom relaunch both passed at 2026-11-08 03:45-03:46 +08:00 without reboot.'
    $result.CurrentBinding = if ($deviceRecords[0].Service -eq 'HidUsb' -and $deviceRecords[0].DriverInf -eq 'oem171.inf' -and $deviceRecords[0].LowerFilters -contains 'AppleDFR') { 'AppleOriginal' } elseif ($deviceRecords[0].Service -eq 'VBoxUSB') { 'UsbIpCustom' } elseif ($deviceRecords[0].Service -eq 'usbccgp') { 'UsbComposite' } else { 'OtherOrUnknown' }
    $result.Limitation = 'Verification flags record completed checks, not a fresh hardware test. Working display and touch volume use local USB/IP; the earlier WinUSB display path failed error 998. Brightness, sleep/resume and reboot behavior are not physically verified. This script only reads status.'
    $result.BindingScript = Join-Path $PSScriptRoot 'BindTouchBarUsbIp.ps1'
    $result.DriverGuide = Join-Path $PSScriptRoot '../TouchBar-USBIP/使用说明.md'
    $result.VerificationEvidence = @(
        (Join-Path $PSScriptRoot '../verification-20261108/after-signed-driver-state.json'),
        (Join-Path $PSScriptRoot '../verification-20261108/repaired-package-flags.json'),
        (Join-Path $PSScriptRoot '../verification-20261108/winusb-physical-touch.json'),
        (Join-Path $PSScriptRoot '../verification-20261108/display-native-buffer-probe.jsonl'),
        (Join-Path $PSScriptRoot '../verification-20261108/bind-Restore.log'),
        (Join-Path $PSScriptRoot '../verification-usbip-20261108/local-display-held.jsonl'),
        (Join-Path $PSScriptRoot '../verification-usbip-20261108/验收结果.md')
    )
}

[pscustomobject]$result | ConvertTo-Json -Depth 8
