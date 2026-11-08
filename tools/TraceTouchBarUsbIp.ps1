[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$logs=Join-Path $root 'verification-usbip-20261108'
$server=$null
try {
    Stop-Service usbipd
    $server=Start-Process -FilePath 'C:\Program Files\usbipd-win\usbipd.exe' -ArgumentList @('server','Logging:LogLevel:Default=Trace') -WindowStyle Hidden -RedirectStandardOutput (Join-Path $logs 'server-trace.txt') -RedirectStandardError (Join-Path $logs 'server-error.txt') -PassThru
    Write-Output ('Debug server PID='+$server.Id)
    $server.Id | Set-Content -LiteralPath (Join-Path $logs 'server-debug.pid')
    if(-not $server.WaitForExit(60000)) {Stop-Process -Id $server.Id}
} finally {
    if($server -and -not $server.HasExited){Stop-Process -Id $server.Id -ErrorAction SilentlyContinue}
    Start-Service usbipd
}
