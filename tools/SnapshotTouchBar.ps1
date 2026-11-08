$ErrorActionPreference = 'Stop'
$targetDir = Join-Path $PSScriptRoot '../verification-20261108'
$snapshotPath = Join-Path $targetDir 'before-device-properties.json'
if (Test-Path -LiteralPath $snapshotPath) { throw 'Original property backup already exists; refusing to overwrite it.' }
$rootKey = 'HKLM:\SYSTEM\CurrentControlSet\Enum\USB\VID_05AC&PID_8302\0000000000000000'
$paths = @($rootKey, ($rootKey + '\Device Parameters'))
$displayRoot = 'HKLM:\SYSTEM\CurrentControlSet\Enum\USB\VID_05AC&PID_8302&MI_01'
if (Test-Path -LiteralPath $displayRoot) {
    foreach ($instance in Get-ChildItem -LiteralPath $displayRoot) {
        $paths += $instance.PSPath + '\Device Parameters'
    }
}
$snapshot = foreach ($path in $paths) {
    if (Test-Path -LiteralPath $path) {
        $key = Get-Item -LiteralPath $path
        foreach ($name in @('LowerFilters','UpperFilters','OriginalConfigurationValue','DeviceInterfaceGUIDs')) {
            $exists = $key.GetValueNames() -contains $name
            $kind = $null
            $value = $null
            if ($exists) { $kind = $key.GetValueKind($name).ToString(); $value = $key.GetValue($name) }
            [pscustomobject]@{ Path = $path; Name = $name; Exists = $exists; Kind = $kind; Value = $value }
        }
    }
}
$snapshot | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $snapshotPath -Encoding utf8
Write-Output $snapshotPath
