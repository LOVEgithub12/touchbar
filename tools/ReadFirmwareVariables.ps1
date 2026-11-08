$ErrorActionPreference = 'Stop'
$touchBarReadResults = foreach ($touchBarVarName in @('PK','SetupMode','SecureBoot')) {
    try {
        $touchBarVar = Get-SecureBootUEFI -Name $touchBarVarName
        [pscustomobject]@{
            Name = $touchBarVarName
            Bytes = $touchBarVar.Bytes.Length
            Value = $(if ($touchBarVarName -eq 'PK') { 'Certificate present; certificate bytes omitted' } else { [BitConverter]::ToString($touchBarVar.Bytes) })
        }
    } catch {
        [pscustomobject]@{ Name = $touchBarVarName; Error = $_.Exception.Message }
    }
}
$touchBarReadResults | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'FirmwareReadOnlyStatus.json') -Encoding UTF8
