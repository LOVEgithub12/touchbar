$ErrorActionPreference = 'Stop'
$cancel = $null
try {
    if ([System.Threading.EventWaitHandle]::TryOpenExisting('Local\WindowsTouchBar.AutoStart.Cancel', [ref]$cancel)) {
        [void]$cancel.Set()
    }
    & (Join-Path $PSScriptRoot 'app\WindowsTouchBar.exe') --stop
} finally {
    if ($null -ne $cancel) { $cancel.Dispose() }
}
