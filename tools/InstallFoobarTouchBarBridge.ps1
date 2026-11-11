$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
$profile = 'C:\Program Files\foobar2000\profile'
$panel = Join-Path $profile 'foobox\script\js_panels\bottombar.js'
$bridge = Join-Path $profile 'foobox\script\js_common\touchbar_bridge.js'
$source = Join-Path $PSScriptRoot 'LyricsResearch\FoobarBridge.js'
$backup = Join-Path $workspace 'backup-20261108\foobar-lyrics'
$evidence = Join-Path $workspace 'verification-usbip-20261108'
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
$marker = '// WindowsTouchBar lyrics bridge integration'
if (-not (Test-Path -LiteralPath $panel) -or -not (Test-Path -LiteralPath $source)) { throw 'Expected foobox panel or bridge source is missing.' }
New-Item -ItemType Directory -Path $backup -Force | Out-Null
$original = [IO.File]::ReadAllText($panel)
if (-not $original.Contains($marker)) {
    $backupPanel = Join-Path $backup 'bottombar.js.original'
    if (Test-Path -LiteralPath $backupPanel) { throw 'Backup already exists but panel integration is absent; refusing to overwrite the previous backup.' }
    Copy-Item -LiteralPath $panel -Destination $backupPanel
    if (Test-Path -LiteralPath $bridge) { Copy-Item -LiteralPath $bridge -Destination (Join-Path $backup 'touchbar_bridge.js.original') }
}
$appDirectory = Join-Path $workspace 'TouchBar-USBIP\app'
$configuredSource = 'var TouchBarBridgeOutputPath = ' + (ConvertTo-Json -InputObject $appDirectory -Compress) + ';' +
    [Environment]::NewLine + [IO.File]::ReadAllText($source)
[IO.File]::WriteAllText($bridge, $configuredSource, (New-Object Text.UTF8Encoding($false)))
if (-not $original.Contains($marker)) {
    $addition = @'

// WindowsTouchBar lyrics bridge integration
try { include(fb.ProfilePath + 'foobox\\script\\js_common\\touchbar_bridge.js'); }
catch (touchBarBridgeError) { console.log('Touch Bar bridge could not start: ' + touchBarBridgeError); }
'@
    [IO.File]::AppendAllText($panel, $addition, (New-Object Text.UTF8Encoding($false)))
}
[pscustomobject]@{
    InstalledAt = (Get-Date -Format o)
    Panel = $panel
    Bridge = $bridge
    PanelBackup = (Join-Path $backup 'bottombar.js.original')
    SourceSHA256 = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
    InstalledSHA256 = (Get-FileHash -LiteralPath $bridge -Algorithm SHA256).Hash
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $workspace 'verification-usbip-20261108\foobar-bridge-install.json') -Encoding UTF8
