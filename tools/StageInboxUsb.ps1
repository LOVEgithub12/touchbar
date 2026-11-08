$ErrorActionPreference = 'Stop'
$source = 'C:\Windows\WinSxS\amd64_dual_usb.inf_31bf3856ad364e35_10.0.19041.5794_none_b96e696680cd53f1\usb.inf'
$published = 'C:\Windows\INF\usb.inf'
$resultFile = Join-Path $PSScriptRoot '../verification-20261108/stage-inbox-usb.txt'
if ((Get-FileHash -LiteralPath $source).Hash -ne (Get-FileHash -LiteralPath $published).Hash) {
    throw 'Windows component INF differs from published INF; refusing staging.'
}
$binary = Join-Path (Split-Path $source) 'usbccgp.sys'
if ((Get-AuthenticodeSignature -LiteralPath $binary).Status -ne 'Valid') {
    throw 'The inbox USB composite driver signature is not valid.'
}
# Import this existing Windows package only. /install is deliberately omitted:
# this command cannot update every device using the package.
$output = & "$env:SystemRoot\System32\pnputil.exe" /add-driver $source 2>&1
$code = $LASTEXITCODE
@("ExitCode: $code", $output) | Set-Content -LiteralPath $resultFile -Encoding utf8
exit $code
