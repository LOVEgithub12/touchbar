[CmdletBinding()]
param([ValidateSet('Enable','Disable')][string]$Mode = 'Enable')
$ErrorActionPreference = 'Stop'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$valueName = 'WindowsTouchBar'
$disabled = Join-Path $PSScriptRoot 'autostart.disabled'
if ($Mode -eq 'Disable') {
    Set-Content -LiteralPath $disabled -Value (Get-Date -Format o) -Encoding ASCII
    if (Test-Path -LiteralPath $runKey) { Remove-ItemProperty -LiteralPath $runKey -Name $valueName -ErrorAction SilentlyContinue }
    Write-Output 'Touch Bar automatic login startup disabled.'
    exit 0
}
$script = Join-Path $PSScriptRoot 'AutoStart.ps1'
$shell = "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe"
$command = '"' + $shell + '" -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File "' + $script + '"'
if ($command.Length -gt 260) { throw 'The automatic startup command exceeds the Windows Run key length limit.' }
if (-not (Test-Path -LiteralPath $script)) { throw 'AutoStart.ps1 is missing.' }
if (-not (Test-Path -LiteralPath $runKey)) { New-Item -Path $runKey | Out-Null }
New-ItemProperty -LiteralPath $runKey -Name $valueName -Value $command -PropertyType String -Force | Out-Null
if (Test-Path -LiteralPath $disabled) { Remove-Item -LiteralPath $disabled }
Write-Output 'Touch Bar automatic login startup enabled for the current desktop user.'
