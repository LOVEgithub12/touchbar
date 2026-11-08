# The native helper currently supports same-class binding only. Do not call Bind for
# a cross-class transition unless the candidate exists in ListCandidates first.
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $PSScriptRoot 'InboxDriverBinding.cs')
$parent = [InboxDriverBinding]::ParentId
$result = [ordered]@{
    ReadOnly = $true
    NativeStructSizes = [InboxDriverBinding]::StructSizes()
    ParentUsbCandidates = @([InboxDriverBinding]::ListCandidates($parent, 'C:\Windows\INF\usb.inf'))
    GlobalUsbCandidates = @([InboxDriverBinding]::ListGlobalCandidates('C:\Windows\INF\usb.inf'))
    ParentAppleCandidates = @([InboxDriverBinding]::ListCandidates($parent, 'C:\Windows\INF\oem171.inf'))
    GlobalWinUsbCandidates = @([InboxDriverBinding]::ListGlobalCandidates('C:\Windows\INF\winusb.inf'))
    ParameterKey = [InboxDriverBinding]::InspectParameterKey($parent)
}
[pscustomobject]$result | ConvertTo-Json -Depth 5
