$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
$build = Join-Path $workspace 'verification-usbip-20261108\controls-build'
$live = Join-Path $workspace 'TouchBar-USBIP\app'
$executable = Join-Path $live 'WindowsTouchBar.exe'
if (-not (Test-Path -LiteralPath (Join-Path $build 'WindowsTouchBar.exe'))) {
    throw 'Build the updated application before deploying.'
}

$running = @(Get-Process -Name WindowsTouchBar -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -eq $executable })
& $executable --stop
foreach ($process in $running) {
    if (-not $process.WaitForExit(10000)) { throw 'The existing Touch Bar application did not stop; files were not replaced.' }
}

foreach ($item in Get-ChildItem -LiteralPath $build) {
    if ($item.Name -ne 'settings.json') {
        Copy-Item -LiteralPath $item.FullName -Destination $live -Recurse -Force
    }
}
$started = Start-Process -FilePath $executable -ArgumentList '--usbip','--media' -WorkingDirectory $live -WindowStyle Hidden -PassThru
for ($attempt = 0; $attempt -lt 40; $attempt++) {
    Start-Sleep -Milliseconds 500
    $status = Get-Content -LiteralPath (Join-Path $live 'WindowsTouchBar.status.log') -Raw
    if ($status -match '(?m)^(RUNNING|FAILED):' -or $started.HasExited) { break }
}
[pscustomobject]@{
    ProcessId = $started.Id
    Running = -not $started.HasExited
    Status = $status
} | ConvertTo-Json
if ($status -notmatch '(?m)^RUNNING:') { throw 'The updated application did not reach RUNNING; inspect its status log.' }
